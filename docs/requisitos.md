# Requisitos do Sistema TicketPrime

## Histórias

### História 1 — Cadastro de conta
Como usuário, Quero cadastrar uma conta informando CPF, nome, e-mail e senha, Para acessar a plataforma e comprar ingressos sem precisar informar meus dados novamente.

**Critérios de Aceitação:**

Dado que o usuário está na tela de cadastro,
Quando ele preencher todos os campos obrigatórios com dados válidos e CPF ainda não cadastrado,
Então a API deve criar a conta e retornar status 201.

Dado que o usuário está na tela de cadastro,
Quando ele informar um CPF que já existe no sistema,
Então a API deve retornar status 400 com a mensagem "Erro: O CPF informado já está cadastrado."

Dado que o usuário está na tela de cadastro,
Quando ele informar uma senha com menos de 6 caracteres,
Então o sistema deve exibir a mensagem "A senha deve ter no mínimo 6 caracteres."

---

### História 2 — Compra de ingresso
Como usuário, Quero comprar um ingresso para um evento disponível, Para garantir minha vaga sem risco de superlotação.

**Critérios de Aceitação:**

Dado que o usuário está autenticado e o evento possui vagas disponíveis,
Quando ele realizar a compra de um ingresso para um evento com data futura,
Então a API deve criar a reserva, diminuir a capacidade do evento em 1 e retornar status 201.

Dado que o usuário tenta comprar um ingresso,
Quando a capacidade do evento for igual a zero,
Então a API deve retornar status 400 com a mensagem "Não há mais vagas disponíveis para este evento."

Dado que o usuário tenta comprar um ingresso,
Quando a data do evento já tiver passado,
Então a API deve retornar status 400 com a mensagem "Este evento já aconteceu."

---

### História 3 — Cancelamento de ingresso
Como usuário, Quero cancelar um ingresso comprado, Para liberar minha vaga e não ser cobrado por um evento que não poderei comparecer.

**Critérios de Aceitação:**

Dado que o usuário possui uma reserva ativa,
Quando ele solicitar o cancelamento da reserva,
Então a API deve remover a reserva do banco e devolver 1 vaga à capacidade do evento.

Dado que o usuário tenta cancelar uma reserva,
Quando o ID da reserva não pertencer ao CPF do usuário autenticado,
Então a API deve retornar status 400 com a mensagem "Não foi possível cancelar a reserva."

---

### História 4 — Listagem dos próprios ingressos
Como usuário, Quero visualizar todos os ingressos que comprei, Para acompanhar minhas reservas ativas e o histórico de eventos.

**Critérios de Aceitação:**

Dado que o usuário está autenticado,
Quando ele acessar a listagem de seus ingressos,
Então a API deve retornar todas as reservas do CPF autenticado com nome do evento, data do evento e preço, usando INNER JOIN com a tabela de Eventos.

---

### História 5 — Cadastro de evento
Como administrador, Quero cadastrar novos eventos informando nome, capacidade, data e preço, Para disponibilizá-los para venda na plataforma.

**Critérios de Aceitação:**

Dado que o administrador está autenticado,
Quando ele preencher todos os campos com dados válidos e data futura,
Então a API deve registrar o evento no banco e retornar status 201.

Dado que o administrador tenta cadastrar um evento,
Quando informar capacidade igual ou menor que zero,
Então o sistema deve lançar a mensagem "A capacidade total deve ser um valor positivo."

Dado que o administrador tenta cadastrar um evento,
Quando informar uma data no passado,
Então o sistema deve lançar a mensagem "A data do evento deve ser no futuro."

---

### História 6 — Cadastro de cupom de desconto
Como administrador, Quero cadastrar cupons de desconto com código, percentual e valor mínimo, Para aplicar promoções a eventos com preço acima do limite definido.

**Critérios de Aceitação:**

Dado que o administrador está autenticado,
Quando ele informar um código único e um percentual válido entre 1 e 100,
Então a API deve cadastrar o cupom e retornar status 201.

Dado que o administrador tenta cadastrar um cupom,
Quando o código já existir no banco de dados,
Então a API deve retornar status 409 com a mensagem "Cupom já existe."

Dado que o administrador tenta cadastrar um cupom,
Quando o percentual de desconto for menor ou igual a zero ou maior que 100,
Então a API deve retornar status 400 com a mensagem "Desconto deve ser entre 0 e 100."
