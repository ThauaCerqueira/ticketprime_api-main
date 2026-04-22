# 🎟️ TicketPrime

Sistema de venda de ingressos rápido, seguro e escalável, desenvolvido como projeto oficial da disciplina de Engenharia de Software.

## 👥 Equipe

| Nome | Matrícula |
|---|---|
| Thauã Cerqueira | 06010400 |
| Felipe Dário | 06009691 |
| Pedro Freitas | 06009656 |
| Pedro Henrique Alves | 06003335 |
| Gabriel | 06009870 |


---

## 🚀 Como Executar

### Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server com instância `SQLEXPRESS`
- [SQL Server Management Studio (SSMS)](https://aka.ms/ssmsfullsetup)

### Banco de Dados

```bash
# Abra o arquivo /db/script.sql no SSMS e execute (F5)
# Isso vai criar o banco TicketPrime, as 4 tabelas e o usuário admin padrão
```

### API (Backend)

```bash
# 1. Clone o repositório
git clone https://github.com/ThauaCerqueira/ticketprime_api.git
cd ticketprime_api

# 2. Entre na pasta da API
cd src

# 3. Execute
dotnet run
```

Swagger disponível em: **http://localhost:5164/swagger**

### Frontend (Blazor)

```bash
cd ui/TicketPrime.Web
dotnet run
```

Acesse no navegador: **http://localhost:5194**

### Testes

```bash
cd tests
dotnet test
```

### Comandos úteis

```bash
dotnet build        # Compilar
dotnet clean        # Limpar build
dotnet restore      # Restaurar pacotes
```

---

## 🔐 Contas de Teste

| Tipo | CPF | Senha |
|---|---|---|
| Administrador | `00000000000` | `admin123` |
| Cliente | Cadastre-se em `/cadastro-user` | — |

---

## 📁 Estrutura do Projeto

```
ticketprime_api/
├── src/                  # Minimal API em C# com Dapper
│   ├── Controllers/      # Controllers auxiliares
│   ├── DTOs/             # Objetos de transferência de dados
│   ├── Infrastructure/   # Repositories e DbConnectionFactory
│   ├── Models/           # Modelos de dados
│   ├── Service/          # Serviços de negócio
│   └── Program.cs
├── ui/                   # Frontend Blazor Server
│   └── TicketPrime.Web/
├── db/                   # Scripts SQL
├── docs/                 # Documentação (requisitos)
└── tests/                # Testes xUnit com Moq
```

---

## 📋 Histórias de Usuário Implementadas

| ID | Papel | Descrição | Status |
|---|---|---|---|
| US-01 | Usuário | Como usuário, quero cadastrar uma conta com CPF e senha para acessar a plataforma | ✅ |
| US-02 | Usuário | Como usuário, quero comprar ingressos para eventos disponíveis garantindo minha vaga | ✅ |
| US-03 | Usuário | Como usuário, quero cancelar um ingresso comprado para liberar minha vaga | ✅ |
| US-04 | Usuário | Como usuário, quero visualizar todos os meus ingressos comprados | ✅ |
| US-05 | Administrador | Como administrador, quero cadastrar eventos com nome, data, capacidade e preço | ✅ |
| US-06 | Administrador | Como administrador, quero cadastrar cupons de desconto com código e percentual | ✅ |

## ✅ Critérios de Aceitação

| ID | Papel | Critério |
|---|---|---|
| US-01 | Usuário | CPF único por cadastro — retorna erro 400 se CPF já existir |
| US-02 | Usuário | Bloqueia compra se evento lotado ou data já passou |
| US-03 | Usuário | Cancela reserva e devolve vaga ao evento automaticamente |
| US-04 | Usuário | Lista ingressos com nome do evento, data e preço via INNER JOIN |
| US-05 | Administrador | Valida capacidade > 0 e data futura antes de salvar |
| US-06 | Administrador | Valida código único e desconto entre 1% e 100% |

---

## ⚙️ Tecnologias

- **Blazor Server** — Frontend com C# e renderização server-side
- **.NET 10 / C#**
- **Dapper** — Acesso ao banco com SQL puro e parâmetros `@`
- **SQL Server** — Banco de dados relacional
- **JWT** — Autenticação via Bearer Token
- **xUnit + Moq** — Testes unitários

---

## 🔄 Metodologia

Modelo **Incremental e Iterativo**, com entregas organizadas por funcionalidade e validação contínua das regras de negócio.

---

## ⚠️ Riscos Identificados

| Risco | Mitigação |
|---|---|
| Superlotação de evento | Controle de capacidade com decremento atômico no banco |
| Compra de ingresso com data expirada | Validação de `DataEvento > DateTime.Now` antes do INSERT |
| CPF duplicado no cadastro | Verificação prévia no banco antes de inserir |
| Fraude com cupom abaixo do valor mínimo | Validação do `ValorMinimoRegra` antes de aplicar desconto |
| SQL Injection | Todas as queries usam parâmetros Dapper com `@` |
