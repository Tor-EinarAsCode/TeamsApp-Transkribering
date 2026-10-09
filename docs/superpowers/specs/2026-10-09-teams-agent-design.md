# Intern Teams-agent – godkjent design

Godkjent av brukeren 9. oktober 2026 gjennom designutkastet i samtalen.

## Mål og avgrensning

Appen er intern i én Microsoft 365-organisasjon. Den installeres i team, chat og møte. En egen lydagent deltar i møtet og transkriberer uten at noen trenger å starte Teams-transkripsjon. I chat hjelper agenten bare ved direkte @omtale. Automatiske møtereferater er et uttrykkelig unntak.

Første versjon støtter planlagte Teams-møter organisert av den interne organisasjonen. Installasjon i et team betyr ikke opptak av alle teamets møter. Arrangøren starter og stopper agenten via møteappen. Agenten vises som møtedeltaker og følger lobbyreglene. Norsk bokmål er standard.

## Møte og referat

Møteappen viser venter, transkriberer, stoppet eller feil. Tale transkriberes med tidsstempler. Talernavn brukes bare når koblingen til faktisk deltaker er pålitelig; ellers brukes ukjent taler. Manglende lyd markeres som hull.

Referatet inneholder oppsummering, beslutninger og oppgaver med ansvarlige og frister der dette faktisk ble sagt. Manglende opplysninger markeres som ikke oppgitt. Referatet er merket AI-generert. Ved tidlig stopp eller avbrudd oppgis perioden som ble fanget og at referatet er ufullstendig. Tomme møter gir ingen oppdiktet oppsummering.

Referatet publiseres i møtechatten og sendes på e-post til identifiserte interne deltakere. Inviterte som ikke deltok mottar ikke automatisk e-post. Eksterne eller anonyme deltakere og deltakere uten verifisert e-postadresse føres i arrangørens oversikt over hvem som ikke kunne nås. Ingen automatisk utsending utenfor organisasjonen i piloten. Feil og usikker leveringsstatus synliggjøres; agenten lover ikke levering som ikke kan dokumenteres.

## Chatmoderator

Agenten svarer på direkte @omtale og hjelper med spørsmål, formuleringer, avklaringer og en konstruktiv diskusjon. Den sletter ikke meldinger og utestenger ingen. Første versjon følger ikke passivt med på alle meldinger og har ikke generell tilgang til hele chathistorikken. Brukeren må inkludere relevant tekst når den tilkalles. Systemhendelser skal ikke utløse ordinære AI-svar.

## Arkitektur

- Teams-pakke: manifest, ikoner, møtefane og bot for team, gruppechat og personlig chat. Intern distribusjon gjennom organisasjonens Teams-administrasjon.
- API/chat: autentisering av Teams-aktivitet og Entra-brukere, organisasjonskontroll, rolle- og møtetilgang, omtaler og sesjonsstyring.
- Medievert: C# og Microsoft Graph Communications Media SDK på Azure Windows Server. Samtalen er bundet til VM-en som opprettet den.
- Azure Speech: mottar lyd fra begrenset kø og produserer endelige tekstsegmenter.
- Bakgrunnsjobber: ferdigstiller transkripsjon, bruker Azure OpenAI til strukturert referat og validerer resultatet før levering.
- Levering: proaktive Teams-meldinger og Microsoft Graph-e-post fra en dedikert avsenderkonto. Varig jobb- og mottakerstatus hindrer unødvendige duplikater.
- Lagring: varig sesjons- og jobbstatus, kryptert transkripsjon og referat, Azure Key Vault for secrets.

Entra-appregistreringen er single-tenant. Organisasjonsgrensen håndheves også i API, hendelsesbehandling og lagring. Tekst fra møter eller chat er innhold og får ikke endre mottakerliste, tilgang eller utløse andre handlinger.

## Plattformkrav og gjennomførbarhet

Microsoft krever Azure Windows Server for produksjon, HTTPS, sertifikater og offentlige medieporter. Azure Web App alene støtter ikke medievert. Media SDK må være nyeste versjon eller maksimalt tre måneder gammel, i tråd med Microsofts krav. Plattformen er dokumentert som developer preview.

Administratorsamtykke kreves til nødvendige rettigheter for møtedeltakelse og lydtilgang, normalt Calls.JoinGroupCall.All og Calls.AccessMedia.All. Mail.Send avgrenses til dedikert avsenderkonto med Exchange-tilgangskontroller. Den endelige rettighetslisten verifiseres mot valgt SDK og integrasjon; generell tilgang til alle chatmeldinger inngår ikke.

Microsofts updateRecordingStatus-dokumentasjon forbyr lagring av media og data utledet fra media før opptaksstatus er satt og bekreftet. Dokumentasjonen knytter API-et til policybasert opptak. Første tekniske milepæl må teste hvilken policy og eventuell støtte eller sertifisering den interne appen krever. Agenten skal ikke lagre transkripsjon hvis status ikke kan bekreftes. Vanlig installasjon av en møtesamtalebot er ikke dokumentert som tilstrekkelig.

Tilgjengelig utviklingsmiljø er Linux og har ingen Azure-identitet eller Azure-tjenester konfigurert. Ekte medieintegrasjon krever derfor separat Azure Windows Server og deres Teams-organisasjon. En lokal simulering erstatter ikke denne testen.

## Lagring og feil

Ingen permanent lydlagring. Transkripsjon og referat slettes fra appens lagring etter 30 dager. Kopier i Teams og e-post følger organisasjonens egne regler. Tilgang begrenses til arrangør og autoriserte deltakere. Region og leverandøroppsett skal følge organisasjonens krav.

Logger inneholder teknisk status uten full møtetekst eller lyd. Hendelser kan komme flere ganger eller i feil rekkefølge. Jobber tåler dette. Ved lydbrudd og kapasitetstap markeres hull. Tjenestefeil gir kontrollert gjenopptakelse og varsling til arrangør. Usikkert utfall av sending skal avklares fremfor å sendes på nytt ukritisk.

## Akseptanse

Et faktisk Teams-møte skal bevise deltakelse, opptaksstatus, norsk lydtranskripsjon, referat og levering til identifiserte interne deltakere. Avbrudd og anonyme deltakere skal gi tydelig begrensning. I gruppechat gir vanlige meldinger null svar, direkte omtale gir hjelp. Tester verifiserer tenant-grense, roller, opptaksstatus før lagring, deltakerutvalg, duplikater, tjenestefeil og instruksjoner gjemt i møteteksten.

## Kilder

- https://learn.microsoft.com/en-us/microsoftteams/platform/bots/calls-and-meetings/requirements-considerations-application-hosted-media-bots
- https://learn.microsoft.com/en-us/graph/api/call-updaterecordingstatus?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/microsoftteams/teams-recording-policy
- https://learn.microsoft.com/en-us/microsoftteams/platform/bots/how-to/conversations/channel-and-group-conversations
