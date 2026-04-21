# 🎟️ TicketPrime

Sistema de venda de ingressos rápido, seguro e escalável, desenvolvido como projeto oficial da disciplina de Engenharia de Software.

---

## 👥 Equipe

| Aluno                | Matrícula  |
|----------------------|------------|
| Thauã Cerqueira      | 06010400   |
| Felipe Dário         | 06009691   |
| Pedro Freitas        | 06009656   |
| Pedro Henrique Alves | 06003335   |
| Gabriel              | 06009870   |

---

## 📋 Visão Geral do Projeto

| Categoria                    | Descrição |
|------------------------------|-----------|
| Contexto do Sistema          | Criar um backend sólido onde clientes possam cadastrar e vender eventos de forma rápida e segura |
| Critérios Determinantes      | Usuário só pode ter um cadastro por CPF, a capacidade de eventos deve ser positiva e maior que zero, o código do cupom não pode ser nulo |
| Maiores Riscos Identificados | Processo judicial, superlotação, vazamento de dados, descontos inexistentes para determinado evento |
| Modelo de Ciclo Recomendado  | Interativo e Incremental |
| Justificativa Técnica        | Escolhido por permitir testes frequentes das funcionalidades e entregas contínuas ao cliente, sendo mais flexível que o modelo cascata e espiral |

---

## 🗂️ Estrutura do Repositório

```
/
├── db/        # Scripts SQL de criação do banco de dados
├── docs/      # Documentação (requisitos, arquitetura, operação)
├── src/       # Código-fonte da Minimal API em C#
├── tests/     # Projeto de testes automatizados (xUnit)
└── ui/        # Frontend Blazor (TicketPrime.Web)
```

---

## ⚙️ Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- SQL Server (local ou remoto)
- String de conexão configurada em `src/appsettings.json`

---

## 🚀 Rodando a API

```bash
cd src
dotnet add package Dapper
dotnet add package Microsoft.Data.SqlClient
dotnet add package Microsoft.Identity.Client
dotnet add package Swashbuckle.AspNetCore
dotnet add package Microsoft.AspNetCore.Cors
dotnet restore
dotnet build
dotnet run
```

A API ficará disponível em: `http://localhost:5164`

Documentação Swagger: `http://localhost:5164/swagger`

---

## 💻 Rodando o Frontend

```bash
cd ui/TicketPrime.Web
dotnet restore
dotnet build
dotnet run
```

---

## 🧪 Rodando os Testes

```bash
cd tests
dotnet add package Moq
dotnet add package Xunit
dotnet restore
dotnet build
dotnet test
```

---

## 🌐 Endpoints da API

| Método | Rota                    | Descrição                            | Auth       |
|--------|-------------------------|--------------------------------------|------------|
| POST   | `/api/usuarios`         | Cadastra um novo usuário             | Público    |
| POST   | `/api/auth/login`       | Realiza login e retorna token JWT    | Público    |
| GET    | `/api/eventos`          | Lista todos os eventos               | Público    |
| POST   | `/api/eventos`          | Cadastra um novo evento              | Admin      |
| POST   | `/api/cupons`           | Cadastra um novo cupom de desconto   | Admin      |
| POST   | `/api/reservas`         | Compra um ingresso                   | Autenticado|
| GET    | `/api/reservas/minhas`  | Lista ingressos do usuário logado    | Autenticado|
| DELETE | `/api/reservas/{id}`    | Cancela um ingresso                  | Autenticado|
| GET    | `/api/perfil`           | Retorna dados do usuário logado      | Autenticado|

---

## 🗄️ Banco de Dados

Execute o script em `/db/script.sql` no SQL Server para criar todas as tabelas e o usuário administrador padrão.

**Credenciais do admin padrão:**

| Campo | Valor                     |
|-------|---------------------------|
| CPF   | `00000000000`             |
| Senha | `admin123`                |
| Email | `admin@ticketprime.com`   |





