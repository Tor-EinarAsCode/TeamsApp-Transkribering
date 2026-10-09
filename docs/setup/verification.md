# Verifikasjon og leveransestatus

Dato: 9. oktober 2026. Koden ligger på feature-grenen `feat/internal-teams-agent`. Ingen Azure-ressurser eller Teams-installasjoner er opprettet i denne sesjonen.

## Utført lokalt

- `dotnet build TeamsAgent.sln`: bygger API, Core, Persistence, Worker, Windows-målrettet MediaHost og tester; null advarsler og feil ved siste fullstendige bygg.
- `dotnet test TeamsAgent.sln`: 58 beståtte tester, fire eksplisitt hoppet over SQL-integrasjonstester, ingen feilede tester.
- API-smoke med lokale testinnstillinger: `/health` svarer 200, møtefanens `/index.html` svarer 200, både `/api/meetings/start` og `/api/messages` svarer 401 uten token. Ingen eksterne API-kall eller faktisk møte foretas av denne testen.
- Bicep-malen kompilerer uten diagnostikk. Dette er syntaks/typevalidering, ikke Azure-deployment eller Azure what-if.
- JavaScript-syntaks, ZIP-innhold, ikoner (192 × 192 og 32 × 32) og et manifest generert med test-ID-er er validert mot Microsofts offisielle Teams-schema 1.19. Test-ID-ene er ikke brukbare appregistreringer.
- API publiseres som Release-artefakt, og medieverten publiseres for win-x64. Publisering til en lokal mappe er ikke installasjon i Azure.

## Uavhengig kodegjennomgang

En separat reviewer gjennomgikk implementasjonen. Følgende funn ble rettet i én regresjonstestet passering:

1. Opptaksforespørsel var ikke bekreftet operasjon: REST-operasjonen spores til completed før gate åpnes; failure/cancellation åpner ikke gate. Test: RequestAcceptedDoesNotConfirmRecording, FailedRecordingOperationCannotOpenGate.
2. Siste talesegmenter kunne mistes: input tømmes til EOF og SessionStopped, og siste resultatlagring avventes før finalisering. Timeout gir ufullstendig status. Test: FinalSpeechWritesAreAwaitedAfterSessionStop.
3. Terminale callbacks kunne starte opptak på nytt: livssyklus serialiseres, ventende start avbrytes og alle avslutninger bruker samme cleanup. Tester: TerminalBeforeEstablishedCannotStart, PendingConfirmationCannotResumeAfterTermination, RosterCallbacksCannotWriteAfterFinalSnapshot.
4. Lobbybrukere kunne feilaktig bli mottakere: faktisk deltakelse registreres først når IsInLobby eksplisitt er false; senere innleggelse følges med participant updates. Tester: LobbyOnlyUserDidNotAttend, UnknownLobbyStateIsNotAttendee, AdmittedUserDidAttend.
5. Slettede sesjoner kunne gjenopprettes: callbacks og fullføring nekter å opprette manglende/utløpte sesjoner. Tester: DeletedSessionIsNeverResurrected, ExpiredSessionCannotBeRevived, DeletedSessionCannotBeRecreatedAfterStop.
6. Vilkårlig tekstbelegg kunne støtte oppdiktede oppgaver: beslutninger/oppgaver gjengis som ordrette utdrag; ansvarlige/frister må finnes i samme oppgaveutdrag. Tester: UnrelatedEvidenceCannotSupportInventedDecision, OwnerMentionElsewhereIsNotAssignment.
7. Parallelle leveringsjobber kunne gi for tidlig rapport: en varig lease dekker mottakeroppslag, sending, arrangørrapport og fullføring. Test: ConcurrentDeliveryCannotPublishPrematureReport.
8. Midlertidig directory-feil kunne bli permanent utelukkelse: 429/5xx gir feil/retry, ikke manglende identitet. Tester: TemporaryDirectoryFailureDoesNotExcludeAttendee, MissingDirectoryIdentityIsUnverifiable.

Regresjonstestene for funnene ble observert feilende før retting og bestått etterpå. Native Teams/Speech-oppførsel er fremdeles en separat live-gate. Ingen ny reviewer-runde er foretatt etter retting; sluttverifikasjonen bygger på regresjonstestene og fullt bygg/test.

## Blokkert

- SQL Server-containeren kunne ikke lastes ned: miljøets nettverk svarte Forbidden. De fire SQL-testene er derfor ikke verifisert, og står som SKIPPED. Testene aktiveres med TEAMSAGENT_TEST_SQL mot en avgrenset testdatabase.
- Ingen Azure-identitet, abonnementstilknytning, Entra-appregistrering eller Microsoft 365-administrator er tilgjengelig i miljøet.
- Ekte medieprobe, opptakspolicy, Windows-native runtime, Azure-tjenester, mailmottak og intern Teams-installasjon er ikke kjørt. Se deployment.md og InternalPilotChecklist.md.

## Implementeringsvalg

- Arbeidet ble gjort på en feature-gren i den eksisterende cloud-checkouten. Ingen ekstra worktree, push eller merge ble utført. Den lokale git-historikken kan gjennomgås før integrasjon.
- Cloud-ferdighetens scripts var ikke lokale filer; progresjon ble ført i en lokal ledger og denne permanente rapporten.
- Pilotens transkripsjon og referat lagres i Azure SQL i stedet for en separat Blob-tjeneste. Det forenkler tilgang og sletting, men gir mer tekstlagring og last i SQL; separate Blob-artefakter kan innføres ved behov.
- Implementasjonen og felles test-fixtures er committet som en sammenhengende leveranse fremfor syv delcommits som ikke ville bygget alene. Live-akseptanse er ikke markert fullført.

Appen skal ikke beskrives som klar til faktisk intern bruk før de blokkerte live-sjekkene er gjennomført med dokumenterte resultater.
