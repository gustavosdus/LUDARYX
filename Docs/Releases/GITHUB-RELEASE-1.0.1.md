# LUDARYX 1.0.1

Atualização de manutenção do LUDARYX.

## Mudanças principais

- aprimoramento do filtro de aplicativos do Windows/Microsoft Store que não são jogos;
- novo sistema de atualização via GitHub Releases;
- download de atualização com progresso visual e opção de cancelamento;
- validação obrigatória do instalador por SHA-256 antes da instalação;
- novo `LUDARYX.Updater.exe`, que aguarda o encerramento do launcher antes de abrir o instalador;
- opção para verificar atualizações automaticamente ao iniciar ou manualmente nas Configurações/Sobre;
- atualização do workflow do GitHub Actions para compilar também o updater.

## Atualização automática

A atualização automática exige que esta release contenha:

- `LUDARYX-1.0.1-Setup.exe`
- `SHA256SUMS.txt`

O arquivo `SHA256SUMS.txt` deve conter o SHA-256 exato do instalador. Se o hash estiver ausente ou não corresponder ao arquivo baixado, o LUDARYX bloqueia a instalação automática.

> Observação: usuários da versão 1.0.0 ainda precisarão instalar a 1.0.1 manualmente. O sistema de atualização passa a valer para as versões posteriores depois que a 1.0.1 estiver instalada.
