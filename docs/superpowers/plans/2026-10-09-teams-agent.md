# Intern Teams-agent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Recommended here: executing-plans, direct execution in the current session.

**Goal:** Build an internal Teams app that transcribes meeting audio, delivers minutes and responds in chats only when mentioned.

**Architecture:** Separate the Windows Graph media host from the ASP.NET Core API, chat agent and durable processing worker. All components enforce one configured Entra tenant; external services sit behind interfaces so failures and access boundaries can be tested independently.

**Tech Stack:** C#, supported .NET version compatible with the current Graph media SDK (selected in Task 1), ASP.NET Core, Microsoft 365 Agents SDK for authenticated messaging, Graph Communications Media SDK, Azure Speech, Azure OpenAI, Azure SQL, Azure Blob Storage, Key Vault, xUnit, Bicep.

**Spec:** `docs/superpowers/specs/2026-10-09-teams-agent-design.md`

## Global Constraints

- Single-tenant Entra registration and server-side tenant enforcement.
- Norsk bokmål is the default; raw audio is not permanently stored.
- Transcripts and minutes expire after 30 days in app storage; copies in Teams/email follow tenant policy.
- Only the meeting organizer starts/stops transcription; joining obeys the lobby.
- Meeting installation is separate from team installation; neither starts all future meetings automatically.
- No derived text persistence before recording status is confirmed; stop persistence when permission/status is lost.
- Production media hosting: Azure Windows Server, public media endpoints and certificates; no Azure Web App media host.
- Media SDK: newest version or no more than three months old.
- Automatic email goes to verified internal actual attendees, not invitees or guests. Unreachable recipients appear in the organizer report.
- Chat AI replies require a direct mention; automatic minutes are an explicit exception. No passive full-chat ingestion or deletion/expulsion.
- AI input cannot alter recipient lists, permissions or trigger tool actions. Missing speakers, owners and deadlines are not invented.
- No real-Teams success claim without the Azure/tenant acceptance run. Current Linux environment has no Azure credentials.

## Review Focus

- Anonymous attendees and display-name collisions: exclude ambiguous email identities and preserve delivery exceptions (Task 5).
- Duplicate and out-of-order callbacks: terminal state cannot restart transcription or create duplicate finalization (Tasks 2 and 3).
- Late joins and late final speech segments: include actual attendees and drain recognized speech before finalization (Tasks 3 and 4).
- Untrusted meeting/chat text requesting data forwarding: cannot modify recipients or tenant authorization (Tasks 4 and 6).
- Restart during email sending: preserve an unknown outcome and notify the organizer rather than blindly resend (Task 5).

## File Map

- `src/TeamsAgent.Core/`: shared session, transcript, minutes and delivery contracts; no vendor SDK dependencies.
- `src/TeamsAgent.Api/`: authenticated Teams/Entra API and messaging handlers.
- `src/TeamsAgent.MediaHost/`: Windows-only Graph media and Azure Speech adapters.
- `src/TeamsAgent.Worker/`: durable finalization, AI generation, delivery and retention jobs.
- `src/TeamsAgent.Persistence/`: Azure SQL job/session state and Blob transcript/minutes storage.
- `teams/`: manifest, icons and organizer meeting UI.
- `infra/`: Bicep resources and documented admin prerequisites.
- `tests/TeamsAgent.Tests/`: deterministic unit/integration tests with fake external adapters.
- `tests/TeamsAgent.Acceptance/`: opt-in live tenant checks and manual evidence checklist.

### Task 1: Establish a real media feasibility gate

**Files:** Create `docs/setup/media-feasibility.md`, `src/TeamsAgent.MediaHost/TeamsAgent.MediaHost.csproj`, `src/TeamsAgent.MediaHost/Probe/MediaProbe.cs`, `tests/TeamsAgent.Acceptance/MediaProbeChecklist.md`, `infra/media-probe.bicep`, `TeamsAgent.sln`, `global.json`.

**Interfaces:** Produces `MediaFeasibilityResult(bool Joined, bool RecordingConfirmed, bool AudioReceived, string EvidencePath)` and `MediaProbe.RunAsync(string meetingJoinUrl, CancellationToken ct)`. Evidence contains technical status only, no meeting content.

- [ ] Document and pin a currently supported .NET/Graph media SDK combination from current Microsoft samples, NuGet package support and a Windows build; record exact versions and upgrade requirement. Do not assume the Linux SDK is sufficient for native media runtime.
- [ ] Define a failing acceptance check requiring `Joined && RecordingConfirmed && AudioReceived`; disabled credentials must show SKIPPED, not PASS.
- [ ] Implement the smallest Windows-only probe using the actual SDK, with no transcript/audio persistence. Test recording-state confirmation through the asynchronous operation, not merely HTTP acceptance.
- [ ] Provision a dedicated test VM after Azure credentials exist; configure Entra, bot registration, DNS, certificates, media networking and necessary tenant recording policy with an administrator. Confirm whether the intended ad-hoc installation scenario is supported; if policy-only or certification restricts it, record BLOCKED and present the constraint before continuing production media work.
- [ ] Run the live acceptance checklist: bot joins a planned internal meeting, obeys lobby, confirms recording state and receives actual audio. Save dated evidence in `docs/setup/media-feasibility.md` without secrets or content. No test account/license means BLOCKED, not completed.
- [ ] Commit the probe, pinned versions and feasibility evidence. Independent API/domain work can proceed while blocked, but the complete app must remain labelled unverified.

### Task 2: Tenant-safe meeting lifecycle and API

**Files:** Create `src/TeamsAgent.Core/Meetings/{MeetingKey,MeetingSession,MeetingCoordinator,IMeetingConnector,IMeetingStore}.cs`, `src/TeamsAgent.Api/{Program.cs,Meetings/MeetingEndpoints.cs,Security/MeetingAccess.cs}`, `src/TeamsAgent.Persistence/Meetings/SqlMeetingStore.cs`, `tests/TeamsAgent.Tests/Meetings/MeetingLifecycleTests.cs` and the corresponding project files.

**Interfaces:** `MeetingKey(Guid TenantId, string MeetingId)`; `MeetingState = Waiting | Transcribing | Stopped | Failed`; `MeetingCoordinator.StartAsync(MeetingKey key, Guid organizerId, CancellationToken ct) -> Task<MeetingSession>`; `StopAsync(MeetingKey key, Guid organizerId, CancellationToken ct) -> Task`; `IMeetingConnector.JoinAsync(MeetingKey key, CancellationToken ct) -> Task<string>` returns call ID. `IMeetingStore` supports tenant-keyed load/save with concurrency version and unique active session constraint. Start/stop endpoints derive caller identity from validated Entra token, never request body.

- [ ] Write tests `ForeignTenantIsRejected`, `NonOrganizerCannotStartOrStop`, `DuplicateStartCreatesOneCall`, `TerminalSessionCannotRestartFromLateCallback` and `RestartLoadsPersistedSession`; assert rejected requests cause zero connector calls and stopped remains stopped.
- [ ] Run `dotnet test tests/TeamsAgent.Tests --filter FullyQualifiedName~MeetingLifecycleTests`; expect the new behavior to fail before implementation.
- [ ] Implement the contracts, coordinator, API authorization and SQL concurrency constraints. Validate token tenant, audience, caller and meeting organizer. Publish only technical status to authorized callers.
- [ ] Repeat the command; require all lifecycle tests PASS.
- [ ] Commit with `feat: add tenant-safe meeting lifecycle`.

### Task 3: Audio and bounded speech ingestion

**Files:** Create `src/TeamsAgent.Core/Transcription/{TranscriptSegment,ITranscriptStore,IRecordingGate}.cs`, `src/TeamsAgent.MediaHost/Meetings/GraphMeetingConnector.cs`, `src/TeamsAgent.MediaHost/Transcription/{AudioPipeline,SpeechTranscriber,GraphRecordingGate}.cs`, `tests/TeamsAgent.Tests/Transcription/AudioPipelineTests.cs`.

**Interfaces:** `TranscriptSegment(string Id, MeetingKey Meeting, TimeSpan Start, TimeSpan End, string Speaker, string Text)`; `ITranscriptStore.AppendAsync(TranscriptSegment segment, CancellationToken ct)` with unique segment ID; `IRecordingGate.EnsureConfirmedAsync(string callId, CancellationToken ct)`; `AudioPipeline.Receive(ReadOnlyMemory<byte> pcm, TimeSpan offset) -> bool`; `CompleteAsync(CancellationToken ct) -> Task`. Return false when bounded queue cannot accept data and persist a gap marker. Audio encoding is selected from the SDK's actual audio socket format and matched to Speech configuration.

- [ ] Write tests `UnconfirmedRecordingPersistsNothing`, `QueueOverflowMarksGap`, `UnmatchedSpeakerUsesUnknown`, `DuplicateFinalSegmentIsStoredOnce`, `LateSpeechIsDrainedBeforeStop` and `LostRecordingStateStopsPersistence`. Assert zero transcript writes before confirmed recording and after lost state.
- [ ] Run `dotnet test tests/TeamsAgent.Tests --filter FullyQualifiedName~AudioPipelineTests`; expect FAIL.
- [ ] Implement SDK connector and gate after Task 1 proves support; implement a bounded channel, buffer ownership/disposal, Speech push stream and final result handling. Maintain participant attribution only for verified mappings. Raw buffers must not reach disk or logs.
- [ ] Repeat unit tests; require PASS. Run Windows probe extended with a short known Norwegian sentence and verify actual text without retaining raw audio.
- [ ] Commit with `feat: add gated meeting transcription`.

### Task 4: Durable finalization and grounded minutes

**Files:** Create `src/TeamsAgent.Core/Minutes/{MeetingMinutes,MinutesJob,IMinutesGenerator}.cs`, `src/TeamsAgent.Worker/Minutes/{FinalizeMeetingJob,AzureMinutesGenerator,MinutesValidator}.cs`, `src/TeamsAgent.Persistence/Jobs/SqlJobStore.cs`, `tests/TeamsAgent.Tests/Minutes/FinalizeMeetingTests.cs`, `tests/TeamsAgent.Tests/Fixtures/norsk-mote.json`.

**Interfaces:** `MeetingMinutes(string Summary, IReadOnlyList<string> Decisions, IReadOnlyList<ActionItem> Actions, bool Incomplete, string CapturedPeriod)`; `ActionItem(string Task, string Owner, string Deadline)`; `IMinutesGenerator.GenerateAsync(IReadOnlyList<TranscriptSegment> segments, bool incomplete, CancellationToken ct) -> Task<MeetingMinutes>`; `FinalizeMeetingJob.RunAsync(MeetingKey key, CancellationToken ct) -> Task`. Durable jobs have unique tenant/meeting/version IDs, leases and retry status; finalization awaits Task 3 completion.

- [ ] Write tests `DuplicateEndEventCreatesOneJob`, `EmptyTranscriptDoesNotInventSummary`, `MissingOwnerAndDeadlineAreNotSpecified`, `EarlyStopMarksIncomplete`, `GenerationFailureRetriesDurably`, `LateSegmentsAreIncluded` and `TranscriptInstructionCannotChangeDelivery`. Use fixture with explicit Norwegian decisions and tasks; missing values must be `ikke oppgitt`.
- [ ] Run `dotnet test tests/TeamsAgent.Tests --filter FullyQualifiedName~FinalizeMeetingTests`; expect FAIL.
- [ ] Implement durable finalization and structured AI output validation. Bound input/output size; chunk long transcripts with retained timestamps and evidence. Never take recipients or tools from model output. Labels are Norwegian and explicitly identify AI-generated content.
- [ ] Repeat tests; require PASS. Evaluate a live model response against the fixture with documented expected decisions, owners and deadlines; do not equate a fake model unit test with live output quality.
- [ ] Commit with `feat: generate grounded meeting minutes`.

### Task 5: Attendee resolution and accountable delivery

**Files:** Create `src/TeamsAgent.Core/Delivery/{Attendee,DeliveryReport,IMinutesSender}.cs`, `src/TeamsAgent.Worker/Delivery/{RecipientResolver,DeliverMinutesJob,GraphMailSender,TeamsMinutesSender}.cs`, `src/TeamsAgent.Persistence/Delivery/SqlDeliveryStore.cs`, `tests/TeamsAgent.Tests/Delivery/DeliveryTests.cs`.

**Interfaces:** `Attendee(string ParticipantId, Guid? TenantId, string? VerifiedEmail, bool ActuallyAttended)`; `RecipientExclusion(string ParticipantId, string Reason)`; `RecipientResolution(IReadOnlyList<string> Emails, IReadOnlyList<RecipientExclusion> Exclusions)`; `RecipientResolver.Resolve(Guid tenantId, IReadOnlyList<Attendee> attendees) -> RecipientResolution`; `DeliveryReport(IReadOnlyDictionary<string, DeliveryOutcome> EmailOutcomes, DeliveryOutcome ChatOutcome, IReadOnlyList<RecipientExclusion> Exclusions)`; `IMinutesSender.SendAsync(MeetingKey key, MeetingMinutes minutes, string deliveryId, CancellationToken ct) -> Task<DeliveryOutcome>` where outcome is `Sent | Failed | Unknown`; `DeliverMinutesJob.RunAsync(MeetingKey key, CancellationToken ct) -> Task<DeliveryReport>`. GraphMailSender also takes a verified recipient address per send; TeamsMinutesSender consumes the stored authorized meeting-chat reference. Directory lookup is tenant-bound and never resolves email from display name alone.

- [ ] Write tests `InviteeWhoDidNotAttendIsExcluded`, `GuestAndAnonymousAreReported`, `SameDisplayNameDoesNotMergeIdentities`, `LateActualAttendeeIsIncluded`, `DuplicateJobDoesNotResend`, `MailTimeoutIsUnknownAndNotBlindlyRetried` and `ChatFailureDoesNotEraseEmailStatus`.
- [ ] Run `dotnet test tests/TeamsAgent.Tests --filter FullyQualifiedName~DeliveryTests`; expect FAIL.
- [ ] Implement durable per-recipient outcomes and guarded delivery from a dedicated mailbox with Exchange-scoped app access. Reuse the validated stored Teams conversation reference for meeting-chat delivery. Notify organizer with exclusions and failures. Do not expose minutes through public links.
- [ ] Repeat tests; require PASS. In a test tenant verify one meeting-chat post and email to actual internal attendees, with an invited absentee and a guest correctly excluded. Confirm the mailbox app cannot send as an unrelated account.
- [ ] Commit with `feat: deliver minutes with recipient tracking`.

### Task 6: Mention-only moderator and Teams meeting package

**Files:** Create `src/TeamsAgent.Api/Chat/{MentionRouter,ModeratorHandler}.cs`, `teams/{manifest.json,color.png,outline.png}`, `teams/meeting/{index.html,meeting.js,meeting.css}`, `tests/TeamsAgent.Tests/Chat/MentionRouterTests.cs`, `docs/setup/teams-installation.md`.

**Interfaces:** Define `Mention(string MentionedId)` and `ValidatedTeamsMessage(Guid TenantId, string SenderId, string Text, IReadOnlyList<Mention> Mentions)` in `src/TeamsAgent.Api/Chat/ValidatedTeamsMessage.cs`. `MentionRouter.IsInvoked(string botId, IReadOnlyList<Mention> mentions) -> bool` uses authenticated activity mention entities, not text matching; `ModeratorHandler.HandleAsync(ValidatedTeamsMessage message, CancellationToken ct) -> Task<string?>`. Meeting UI consumes authorized start/stop/status endpoints from Task 2 via Teams SSO; identities are not trusted from browser body fields.

- [ ] Write tests `OrdinaryMessageGetsNoReply`, `LiteralAtTextIsNotMention`, `AnotherBotMentionGetsNoReply`, `DirectMentionGetsHelp`, `ForeignTenantGetsNoAiCall`, `SystemEventDoesNotReply` and `ChatInstructionCannotForwardMinutes`. Assert no AI calls for every uninvoked/rejected message.
- [ ] Run `dotnet test tests/TeamsAgent.Tests --filter FullyQualifiedName~MentionRouterTests`; expect FAIL.
- [ ] Implement authenticated Agents SDK messaging adapter, mention routing, bounded moderator input and Norwegian responses. Add minimal organizer meeting UI with start/stop/status and honest failures; other participants cannot start/stop. No full-message-read permissions.
- [ ] Validate the Teams manifest against the current supported schema and package real correctly sized icons. Run tests; require PASS. Sideload internally and verify ordinary channel/group messages produce no reply and @mention does; verify install in a team does not start a call.
- [ ] Commit with `feat: add Teams meeting controls and mention-only moderator`.

### Task 7: Retention, deployment and complete acceptance

**Files:** Create `src/TeamsAgent.Worker/Retention/RetentionJob.cs`, `infra/{main.bicep,parameters.example.json}`, `docs/setup/{deployment.md,operations.md}`, `tests/TeamsAgent.Tests/Retention/RetentionTests.cs`, `tests/TeamsAgent.Acceptance/InternalPilotChecklist.md`; update `README.md`.

**Interfaces:** `RetentionJob.RunAsync(DateTimeOffset now, CancellationToken ct) -> Task<int>` deletes transcript and minutes blobs plus content-bearing app records aged at least 30 days, preserving minimal technical delivery audit as tenant policy allows. Active meeting or job content is not kept indefinitely; expiry stops processing and removes content. API must immediately deny access to expired content before physical deletion completes.

- [ ] Write tests `ContentExpiresAtThirtyDays`, `TwentyNineDayContentIsRetained`, `ForeignTenantCannotReadContent`, `ExpiredContentCannotBeServed`, `DeletionIsRetryable` and `TechnicalLogsContainNoTranscript`.
- [ ] Run `dotnet test tests/TeamsAgent.Tests --filter FullyQualifiedName~RetentionTests`; expect FAIL.
- [ ] Implement UTC retention scheduling, tenant-bound private storage, Key Vault bindings, SQL/Blob/AI/Speech resources and separate Windows media VM provisioning. Require region, tenant ID, mailbox, certificate and DNS parameters; never embed secrets in examples. Document backups and app retention limitations versus mailed copies.
- [ ] Run `dotnet test TeamsAgent.sln`, the pinned Windows media build, and `az bicep build --file infra/main.bicep`; require PASS/success. Any unavailable check is recorded as blocked, not passed.
- [ ] Run the full live acceptance checklist with at least two internal users, a guest and an invited absentee: join/lobby, recording confirmation, Norwegian transcription, early stop, audio failure, grounded minutes, recipients, moderator mentions, tenant denial and delivery evidence. Record observed limits and administrator prerequisites.
- [ ] Commit with `feat: prepare internal Teams agent pilot`; README distinguishes implemented/tested behavior from blocked external setup. Do not claim ready for actual internal use until live acceptance passes.

## Handoff

Recommended execution: direct implementation in this session using executing-plans. User must review this plan and select the execution method before product implementation. Task 1's live gate requires Azure/Teams administrator setup; code work alone does not satisfy it. The prototype may reveal a Microsoft policy restriction that needs a revised design rather than an attempt to bypass recording requirements.
