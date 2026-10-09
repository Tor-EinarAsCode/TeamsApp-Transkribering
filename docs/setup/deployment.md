# Oppsett i Azure og Microsoft 365

## Før oppsett

Velg én tenant, pilotbrukere, region og en dedikert avsenderkonto. Norway East er foreslått for øvrige tjenester; Azure OpenAI-modell og versjon må finnes i en region organisasjonen har godkjent. Ingen region er bekreftet av administrator ennå. Ikke send secrets i chat eller legg dem i git. Bruk Key Vault eller lokal secret manager.

1. Registrer en single-tenant Entra-app for API/Teams-bot. Sett accessTokenAcceptedVersion=2. Eksponer access_as_user som delegert scope og registrer Teams-klientene som forhåndsgodkjente klienter etter Teams SSO-veiledningen. API URI må være `api://API_HOST/BOT_APP_ID`. Klient-ID-en brukes både i botregistrering og manifest.
2. Registrer en separat single-tenant app for medieverts kontroll-API og sett accessTokenAcceptedVersion=2 også på denne. Eksponer approllen Media.Control (application) og sett API URI `api://MEDIA_APP_ID`. Gi bot/API-appen denne rollen. Kontrolltokenets audience må samsvare med Media:Audience på begge sider. Tilpass tokenversjon/audience eksplisitt ved v2-registrering; test 401 for feil audience og 403 for feil rolle/azp.
3. Gi botappen Calls.JoinGroupCall.All og Calls.AccessMedia.All, OnlineMeetings.Read.All for arrangørens møteoppslag, User.Read.All for verifiserte interne adresser og Mail.Send. Grant admin consent. OnlineMeetings.Read.All krever en Teams application access policy for pilotarrangørene; avgrens den til pilotbrukere. Medieplattformen bruker botappens credentials til Graph, ikke kontrollappens.
4. Avgrens Mail.Send til én dedikert avsenderkonto med Exchange application RBAC. Fjern eller begrens eventuell annen bred Entra/Exchange-autorisering som gir union av rettigheter. Test at appen ikke kan sende som en annen konto.
5. Avklar opptakspolicy og eventuell støttet opptaksløsning/sertifisering med Microsoft. updateRecordingStatus må støttes og være bekreftet før transkripsjon lagres. Ikke slå på policyverified for å omgå en avvist forespørsel.

## Ressurser

`infra/main.bicep` lager Windows Server 2022 VM, offentlig medie-IP, privat nettverk, App Service for API, privat Azure SQL-endepunkt og DNS, Key Vault med RBAC, Azure Speech, Azure OpenAI-modell og single-tenant Azure Bot med Teams-kanal. Modellen er parameterstyrt; standardmodell og versjon må kontrolleres mot regionens tilgjengelighet før deployment.

Bruk `infra/parameters.example.json` som mal. Erstatt alle REPLACE-verdier. Bruk referanser til en eksisterende deployment-Key Vault for sikre passord og appsecret. Region er en eksplisitt parameter ved deployment. Kjør Bicep build og Azure what-if før deployment. Dette repositoryet kjører ikke deployment automatisk.

SQL har ingen offentlig inngang. App Service er integrert i API-subnettet og VM har tilgang via privat nettverk. RDP er ikke åpent; bruk Azure Run Command eller organisasjonens godkjente administrative tilgang. Port 443 og medieport 8445 er åpne for medieplattformen; avgrens ytterligere etter Microsofts gjeldende nettverkskrav uten å blokkere nødvendige tilkoblinger.

Speech/OpenAI krever API-nøkler fra Key Vault og bruker TLS-endepunkter; disse endepunktene er offentlige i denne pilotmalen. Konfigurer private endepunkter/tilgangsregler dersom organisasjonens krav tilsier det. Key Vault har soft-delete/purge protection. Appens 30-dagers sletting gjelder ikke automatisk SQL-backuper eller Key Vault-historikk; administrator må fastsette deres retention separat.

## Database

Som SQL Entra-administrator, opprett databasebrukere for API- og medie-VM-enes managed identities i `teamsagent`. Initialiser skjema én gang med `dotnet TeamsAgent.Api.dll --init-db` fra en administrativ identitet med DDL-tilgang. Runtime-identitetene trenger kun SELECT/INSERT/UPDATE/DELETE i appens tabeller, ikke db_owner eller DDL. Initialisering må kjøres i privat nettverk med en autorisert tilkoblingsstreng.

Produksjonsstrengen er:
`Server=tcp:SQL_SERVER.database.windows.net,1433;Database=teamsagent;Authentication=Active Directory Managed Identity;Encrypt=True;`

## API

Publiser `dotnet publish src/TeamsAgent.Api -c Release` til App Service. Bicep setter tenant, app-ID, Key Vault-referanser, SQL, AI, avsenderkonto og medieendepunkt. Jobs:Enabled starter som false; slå først på etter databaseoppsett og leveringsverifikasjon. Bruk Application Insights eller godkjent logging uten å logge request bodies, lyd eller møtetekst.

## Windows medievert

Publiser `dotnet publish src/TeamsAgent.MediaHost -c Release -r win-x64 --self-contained false`. Installer støttet .NET 8 ASP.NET runtime på Windows VM. Opprett DNS A-record for Media:Fqdn mot Bicep-outputens offentlige IP. Installer et gyldig TLS-sertifikat med privat nøkkel i LocalMachine/My. SDK må kunne lese nøkkelen, og HTTPS/Kestrel må bruke riktig sertifikat. Legg det samme sertifikatet/vertsnavnet til grunn for medieportoppsettet.

Kjør som en Windows-tjeneste med organisasjonens tjenesteverktøy, eller via Task Scheduler med auto-start/restart. Prosjektet er en vanlig ASP.NET Core-host, ikke en ferdig SCM-wrapper. Slå på nødvendige Windows Firewall-regler for 443 og medieport 8445. VM-koden installeres ikke av Bicep.

Nødvendige miljøinnstillinger (double underscore for nested config):

- TenantId, ClientId (botapp-ID), ClientSecret (hentes fra Key Vault uten utskrift).
- ControlClientId (API/botapp-ID), Media__Audience (kontroll-API-tokenets eksakte audience).
- ConnectionStrings__Sql (managed identity, databasebruker må være opprettet).
- Media__Fqdn, Media__PublicIp, Media__Port=8445, Media__CertificateThumbprint.
- Media__NotificationUrl=https://MEDIA_HOST/graph/callback.
- Speech__Key (Key Vault), Speech__Region.
- Media__RecordingPolicyVerified=false frem til godkjent live-probe.
- ASPNETCORE_URLS=https://0.0.0.0:443 og Kestrel__Certificates__Default__Path/Password, eller en tilsvarende sikker sertifikatkonfigurasjon. Ikke legg sertifikatpassord eller PFX i repoet.

SDK må være nyeste versjon eller ikke eldre enn tre måneder. Verifiser Windows native dependency loading og faktisk samtale på VM-en. Kjør proben i media-feasibility.md før vanlig agentstart. En VM-restart avbryter aktive samtaler; se operations.md.

## Før intern installasjon

Kontroller botendepunkt `/api/messages`, mediecallback, SSO-scope/audience og HTTPS. Pakk faktisk manifest og ikoner og distribuer til pilotbrukere via Teams admin center. Ingen offentlig App Store-publisering inngår. Test alt i InternalPilotChecklist.md før resten av organisasjonen får tilgang.
