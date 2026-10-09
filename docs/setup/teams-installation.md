# Teams-pakke og intern distribusjon

1. Publiser backend og fullfør deployment.md først.
2. Lag en stabil UUID for Teams-app-ID; bot-ID er botappens Entra client ID.
3. Kjør scripts/package-teams.py med faktisk API-host. ZIP-en inneholder manifest.json, color.png (192 × 192) og outline.png (32 × 32, transparent).
4. Valider manifestet mot Teams-schema og Developer Portal-validering. Malen bruker manifest 1.19 og ingen generelle rettigheter til å lese alle chatmeldinger.
5. Last opp til organisasjonens interne appkatalog og avgrens tilgjengelighet til pilotbrukerne. Sideinnlasting krever organisasjonens apppolicy.
6. Legg boten til i team/gruppechat. Direkte @omtale utløser hjelp. I personlig chat krever også denne første versjonen omtale; ikke anta vanlig personlig melding utløser AI.
7. Legg møtefanen til i et planlagt internt Teams-møte. Arrangøren limer inn møtelenken og trykker Start møteagent. Agenten følger lobbyreglene. Installasjon starter ikke automatisk opptak.

Automatisk referat i møtechatten krever at en autentisert Teams-aktivitet har gitt appen en lagret samtalereferanse. Kontroller dette under installasjon/live-test; en fane alene garanterer ikke at boten er lagt til chatten. Manglende referanse rapporteres som leveringsfeil, aldri som suksess.
