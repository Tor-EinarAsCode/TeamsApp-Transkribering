# Gjennomførbarhet for ekte møtelyd

Status 9. oktober 2026: **BLOKKERT for live-verifikasjon**. Utviklingsmiljøet er Linux, uten Azure-identitet eller en Microsoft 365-testorganisasjon.

Koden er målrettet mot .NET 8 (SDK 8.0.425) og Microsoft.Graph.Communications.Calls.Media 1.2.0.18736, den nyeste versjonen observert på NuGet. Pakken har et eksplisitt net8.0-mål. Hele Windows-prosjektet kan krysskompileres fra Linux, men den native medieplattformen er ikke kjørt her. Versjonen må oppdateres etter Microsofts maksimale tre måneders alderskrav før pilotinstallasjon dersom nyere versjon finnes.

En administrator må først etablere appregistreringer, sertifikater, nettverk, Windows VM og Teams-tilgang beskrevet i deployment.md. Avklar med Microsoft hvilken policybasert opptaksstøtte og eventuell sertifisering som kreves for denne internt inviterte møteagenten. Ikke anta at Calls.AccessMedia.All alene gir rett til å lagre transkripsjon.

Kjør `POST /control/probe` på medieverten med et Entra-apptoken med Media.Control og en autorisert intern MeetingSession (tenant, møte-ID, arrangør, join.threadId og join.joinUrl). Proben bruker ekte Media SDK og opptaksstatus. Den teller kun mottatte lydbytes; ingen lydkopi eller transkripsjon lagres. Arrangøren må slippe agenten gjennom lobbyen og snakke i minst 15 sekunder etter bekreftet opptaksstatus.

Akseptanse krever at Joined, RecordingConfirmed og AudioReceived alle er true. Lagre teknisk respons, dato, SDK-versjon, Windows-versjon, policyoppsett og eventuelle begrensninger. Bekreft at deltakerne ser opptaksvarsling. HTTP 200 fra en kontrollrute eller en kompilering er ikke tilstrekkelig.

Først etter dokumentert vellykket test og avklart brukerstøtte settes Media__RecordingPolicyVerified=true på medieverten. Vanlig møtestart er sperret når denne innstillingen er false. Proben er en avgrenset teknisk test, ikke en omgåelse av Microsofts policykrav.

Kilder:
- https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/requirements-considerations-application-hosted-media-bots
- https://learn.microsoft.com/en-us/graph/api/call-updaterecordingstatus?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/microsoftteams/teams-recording-policy
