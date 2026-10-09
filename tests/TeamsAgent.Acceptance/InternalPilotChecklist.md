# Akseptanse for intern pilot

Status: IKKE KJØRT. Krever Azure Windows Server og Microsoft 365-tilgang. Fyll inn dato, tenant, SDK-versjon, godkjent region og teknisk evidens ved hver test; ingen lyd, secrets eller full møtetekst i evidens.

- [ ] MediaProbe: Joined, RecordingConfirmed og AudioReceived=true; opptaksvarsling synlig; ingen innhold lagret.
- [ ] Arrangøren kan starte/stoppe; annen intern bruker avvises; annen tenant avvises.
- [ ] Installert teamapp starter ingen møter automatisk. Bot i lobby venter til tillatt.
- [ ] To interne deltakere snakker norsk om en eksplisitt beslutning, én oppgave med ansvarlig og frist og én uten. Transkripsjon og referat samsvarer; manglende verdier er ikke oppgitt.
- [ ] Stopp midt i møtet og lydbrudd gir ufullstendig referat med fanget periode. Siste talesegment før stopp blir med.
- [ ] Gjenta slutthendelser; kun ett referat og ett automatisk sendeforsøk per mottaker.
- [ ] Identifiserte faktiske interne deltakere får e-post; invitert fraværende, gjest og anonym får ikke automatisk e-post; arrangøren får korrekt oversikt.
- [ ] Bekreft både Graph-aksept og faktisk mottak i testinnbokser. Test avvist mailboxrettighet og timeout; Unknown fører ikke til automatisk resending.
- [ ] Møtechatpost vises én gang. Manglende samtalereferanse rapporteres som feil.
- [ ] Vanlige gruppe-/kanalmeldinger og tekst som bare ligner en omtale gir null svar. Direkte @omtale gir hjelp. Systemhendelser gir ingen velkomst-/AI-meldinger.
- [ ] Møtetekst/chattekst som ber om ekstern videresending eller tilgangsoverstyring endrer ingen mottakere/rettigheter.
- [ ] Innhold som er 30 dager gammelt kan ikke leses og slettes fra appens SQL-tabeller; innhold på 29 dager beholdes. Backupregler dokumenteres separat.
- [ ] SMTP/Graph-rettigheter er avgrenset til avsenderkonto, SQL-identiteter har nødvendige runtime-tilganger og secrets kommer fra Key Vault.
- [ ] Windows native Media SDK kjøres med riktig sertifikat, port og oppdatert pakke. VM-restart/avbrudd håndteres med tydelig ufullstendig status.

Ingen pilot kan betegnes klar for reell bruk før disse sjekkene har dokumentert resultat, eller en eksplisitt akseptert begrensning er beskrevet for pilotbrukerne.
