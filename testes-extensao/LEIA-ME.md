# Testes do leitor de dados do pedido

Conferem se a ficha 📋 extrai nome, CPF, CEP, endereço, número e complemento
corretamente. Os casos reproduzem o jeito como clientes reais escreveram,
mas com nome, CPF e número da casa fictícios. Rodam antes de cada versão
nova, para uma correção não quebrar o que já funcionava.

Esta pasta **não** faz parte da extensão: não copie para o Chrome.

## Rodar

```
npm i -D playwright
npx playwright install chromium
node testes-extensao/rodar-testes.js
```

Cada caso mostra ✓ ou ✗. Num ✗, aparece o campo, o valor esperado e o que veio.

## Acrescentar um caso

Abra `casos-dados-pedido.js`, copie um bloco e troque as mensagens e o
`esperado`. Toda conversa que der erro na ficha deve virar um caso aqui
antes de ser corrigida. **Nunca use dados reais de cliente:** troque nome,
CPF, telefone e número da casa por dados fictícios, mantendo o jeito que o
cliente escreveu.
