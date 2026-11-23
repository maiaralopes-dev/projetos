// Casos baseados em conversas reais, com nomes, CPFs e números TROCADOS
// por dados fictícios (mesmo formato de escrita do cliente), e o resultado
// esperado da ficha de dados do pedido. Nunca coloque dados reais aqui.
//
// Para acrescentar um caso: copie um bloco, troque as mensagens e escreva
// em "esperado" só os campos que importam. Campo com "" significa que
// deve ficar vazio. Campos não listados não são conferidos.
//
// "cep" simula a resposta do ViaCEP para o CEP do caso.

const PEDIDO_DADOS =
  "Para finalizar, me envie:\nNome completo:\nRua:\nNúmero:\nBairro:\nCidade:\nEstado:\nCEP da Rua:\nCPF: (para nota fiscal)\nE-mail:";

const PEDIDO_DADOS_GENTILEZA =
  "*Me envie por gentileza:*\n\nSeu Nome completo:\nRua:\nNúmero:\nBairro:\nCidade:\nEstado:\nCEP da Rua:\nCPF: (para nota fiscal)\nE-mail:";

const CEP_PITANGUEIRAS = {
  "21930360": {
    zip: "21930-360",
    street: "Praia das Pitangueiras",
    neighborhood: "Pitangueiras",
    city: "Rio de Janeiro",
    state: "RJ"
  }
};

const CEP_COPACABANA = {
  "22031011": {
    zip: "22031-011",
    street: "Rua Figueiredo Magalhães",
    neighborhood: "Copacabana",
    city: "Rio de Janeiro",
    state: "RJ"
  }
};

const CASO_A =
  "CARLOS MENDES DA ROCHA\nPRAIA DA PITANGUEIRAS N.418 .APT 202\nILHA DO GOVERNADOR\nRJ\nCEP 21930 360\nCPF 529 982 247 .25";

const CASO_A_ESPERADO = {
  name: "Carlos Mendes da Rocha",
  cpf: "529.982.247-25",
  zip: "21930-360",
  street: "Praia das Pitangueiras",
  number: "418",
  complement: "Apt 202",
  neighborhood: "Pitangueiras",
  city: "Rio de Janeiro",
  state: "RJ"
};

function enviada(id, hora, texto, anexos = []) {
  return { id, received: false, text: texto, createdAt: `2026-11-14T${hora}:00Z`, attachments: anexos };
}

function recebida(id, hora, texto, anexos = []) {
  return { id, received: true, text: texto, createdAt: `2026-11-14T${hora}:00Z`, attachments: anexos };
}

// Variações de número/complemento sobre o mesmo endereço.
function variacaoNumero(linhaEndereco, number, complement) {
  return {
    nome: `Número/complemento: "${linhaEndereco}"`,
    cep: CEP_PITANGUEIRAS,
    mensagens: [
      enviada("p1", "09:50", PEDIDO_DADOS),
      recebida("r1", "10:02", `Maria da Silva Souza\n${linhaEndereco}\nCEP 21930 360\nCPF 390 533 447 05`)
    ],
    esperado: { name: "Maria da Silva Souza", number, complement }
  };
}

module.exports = [
  {
    nome: "Caso A: resposta em linhas logo após o pedido de dados",
    cep: CEP_PITANGUEIRAS,
    mensagens: [
      enviada("p1", "09:50", PEDIDO_DADOS),
      recebida("r1", "10:02", CASO_A)
    ],
    esperado: CASO_A_ESPERADO
  },
  {
    nome: "Caso A: atendente envia print do cadastro depois (bug da V16.23)",
    cep: CEP_PITANGUEIRAS,
    mensagens: [
      enviada("p1", "09:50", PEDIDO_DADOS),
      recebida("r1", "10:02", CASO_A),
      recebida("r2", "10:03", "", [{ id: "au1", url: "https://x/a.ogg", type: "audio" }]),
      enviada("e1", "10:24", "até 2 dias corridos, Frete Grátis!"),
      enviada(
        "e2",
        "10:25",
        "Formulário Novo Perfil com os campos CPF 529.982.247-25, Nome completo CARLOS MENDES DA ROCHA, E-mail, CEP 21930-360, Rua Praia das Pitangueiras, Número 418, Bairro Pitangueiras, UF RJ, Cidade Rio de Janeiro",
        [{ id: "im1", url: "https://x/print.png", type: "image" }]
      )
    ],
    esperado: CASO_A_ESPERADO
  },
  {
    nome: "Caso A: print do atendente sem anexo e sem números parece um pedido",
    cep: CEP_PITANGUEIRAS,
    mensagens: [
      enviada("p1", "09:50", PEDIDO_DADOS),
      recebida("r1", "10:02", CASO_A),
      enviada("e1", "10:25", "Formulário Novo Perfil com campos CPF, Nome completo, E-mail, CEP, Rua, Bairro, Cidade")
    ],
    esperado: CASO_A_ESPERADO
  },
  {
    nome: "Caso A: pedido reenviado e cliente só respondeu \"ok\"",
    cep: CEP_PITANGUEIRAS,
    mensagens: [
      enviada("p1", "09:50", PEDIDO_DADOS),
      recebida("r1", "10:02", CASO_A),
      enviada("p2", "10:25", PEDIDO_DADOS),
      recebida("r2", "10:26", "ok")
    ],
    esperado: CASO_A_ESPERADO
  },
  {
    nome: "Caso A: CRM entrega a mensagem do cliente sem indicar quem enviou",
    cep: CEP_PITANGUEIRAS,
    mensagens: [
      enviada("p1", "09:50", PEDIDO_DADOS),
      { id: "r1", received: null, text: CASO_A, createdAt: "2026-11-14T10:02:00Z", attachments: [] }
    ],
    esperado: CASO_A_ESPERADO
  },
  {
    nome: "Caso B: endereço com vírgula, CPF em mensagem separada e conversa depois",
    cep: CEP_COPACABANA,
    mensagens: [
      enviada("p1", "10:17", PEDIDO_DADOS_GENTILEZA),
      recebida("r1", "12:16", "RICARDO ALVES PEREIRA\nRua Figueiredo Magalhães, 615\nCopacabana\nRio de Janeiro - RJ\n22031-011"),
      recebida("r2", "12:16", "CPF:  111.444.777-35"),
      recebida("r3", "12:17", "Lembrando que dinheiro só na sexta feira"),
      recebida("r4", "12:17", "Estarei nesse endereço até às 8 horas da manhã.\nDepois saio e só voltou as 10 horas da noite"),
      recebida("r5", "12:18", "Se poder agendar pras 7/8 horas eu agradeco")
    ],
    esperado: {
      name: "Ricardo Alves Pereira",
      cpf: "111.444.777-35",
      zip: "22031-011",
      street: "Rua Figueiredo Magalhães",
      number: "615",
      complement: "",
      neighborhood: "Copacabana",
      city: "Rio de Janeiro",
      state: "RJ",
      email: ""
    }
  },
  variacaoNumero("PRAIA DA PITANGUEIRAS 305 casa 2", "305", "Casa 2"),
  variacaoNumero("Praia das Pitangueiras, 45", "45", ""),
  variacaoNumero("praia das pitangueiras 120 B", "120", "B"),
  variacaoNumero("Praia das Pitangueiras nº 77 bloco 3 apto 12", "77", "Apt 12, Bloco 3"),
  variacaoNumero("Praia das Pitangueiras apt 104", "", "Apt 104"),
  variacaoNumero("Praia das Pitangueiras numero 9", "9", ""),
  variacaoNumero("Praia das Pitangueiras 305", "305", "")
];
