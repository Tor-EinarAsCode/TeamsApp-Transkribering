# Intern Teams-app: møteagent og chatmoderator

C#/.NET 8-implementasjon for én Microsoft 365-organisasjon. Agenten mottar møtelyd via Graph Media SDK på Azure Windows Server, bruker Azure Speech til norsk transkripsjon, lager referat med Azure OpenAI og publiserer i møtechatten og på e-post til identifiserte interne deltakere. I chat svarer den bare ved direkte @omtale.

**Status:** kode og lokal verifikasjon er tilgjengelig. Appen er ikke installert i Teams og ekte møtelyd er ikke verifisert. Ingen Azure- eller Microsoft 365-identitet er konfigurert i utviklingsmiljøet. Se [mediegate](docs/setup/media-feasibility.md). Produksjonsstart krever dokumentert opptakspolicy og bestått live-test; programvaren sperrer møtestart når Media:RecordingPolicyVerified er false.

## Bygg og test

Installer .NET 8 SDK og kjør fra prosjektroten:

```sh
dotnet restore TeamsAgent.sln
dotnet build TeamsAgent.sln
dotnet test TeamsAgent.sln
```

Medieverten krysskompileres på Linux, men kjører kun på Windows x64. API og øvrige biblioteker kan kjøres på Linux. SQL-integrasjonstestene krever en lokal SQL Server; uten testtilkobling vises de som SKIPPED og teller ikke som verifisert lagring.

## Prosjekt

- `src/TeamsAgent.Core`: tenant-grense, møtelivssyklus, transkripsjonsport, moderatorpolicy, referatvalidering og mottakere.
- `src/TeamsAgent.Persistence`: tenant-avgrenset Azure SQL-lagring med varig jobb-, transkripsjons- og leveringsstatus.
- `src/TeamsAgent.Worker`: referatgenerering, Azure OpenAI-klient, finalisering og utsending.
- `src/TeamsAgent.Api`: autentiserte Teams-aktiviteter, arrangørens API, Graph-klient, utsending og bakgrunnsjobber.
- `src/TeamsAgent.MediaHost`: Windows Graph Media SDK, opptaksstatus, begrenset lydkø og Azure Speech.
- `teams`: norsk møtefane, ikoner og manifestmal.
- `infra`: Bicep for Azure-ressurser. Kode lastes opp etter ressursoppsett; VM-program, DNS og sertifikat installeres separat.

[Installasjonsveiledning](docs/setup/deployment.md), [Teams-pakke](docs/setup/teams-installation.md), [drift og begrensninger](docs/setup/operations.md) og [live-pilot](tests/TeamsAgent.Acceptance/InternalPilotChecklist.md).

## Pakke for intern installasjon

```sh
python scripts/package-teams.py --app-id YOUR_TEAMS_APP_ID --bot-id YOUR_BOT_APP_ID --host YOUR_API_HOST --output work/teams-agent.zip
```

UUID-er og faktisk offentlig API-vertsnavn er nødvendig. Malen er ikke en ferdig installasjonspakke. Pakken har kun manifest og ikoner; backend må være konfigurert og publisert før installasjon. Ingen kontoer eller ressurser endres av pakkeskriptet.
