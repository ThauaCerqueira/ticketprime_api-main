| Aluno                 | Matrícula  |
|---------------------------|--------------|
| Thauã Cerqueira      | 06010400 |
| Felipe Dário  | 06009691  |
| Pedro Freitas | 06009656 |
| Pedro Henrique Alves | 06003335 |
| Gabriel   | 06009870 |





| Categoria                  | Descrição                                                                 |
|---------------------------|--------------------------------------------------------------------------|
| Contexto do Sistema       | Criar um backend sólido onde clientes possam cadastrar e vender eventos de forma rápida e segura |
| Critérios Determinantes   | Usuário só pode ter um cadastro por CPF, a capacidade de eventos deve ser positiva e maior que zero, o código do cupom não pode ser nulo.                                |
| Maiores Riscos Identificados | Processo judicial, superlotação, vazamento de dados, descontos inexistentes para determinado evento |
| Modelo de Ciclo Recomendado | Interativo e Incremental                                               |
| Justificativa Técnica     | Escolhido por permitir testes frequentes das funcionalidades e entregas contínuas ao cliente, sendo mais flexível que o modelo cascata e espiral |


<h2>🚀 Rodando a API</h2>

<pre><code id="api-commands">cd src
dotnet add package Dapper
dotnet add package Microsoft.Data.SqlClient
dotnet add package Microsoft.Identity.Client
dotnet add package Swashbuckle.AspNetCore
dotnet add package Microsoft.AspNetCore.Cors
dotnet restore
dotnet build
dotnet run</code></pre>



<hr/>

<h2>💻 Rodando o Frontend</h2>

<pre><code id="frontend-commands">cd ui
cd TicketPrime.Web
dotnet restore
dotnet build
dotnet run</code></pre>



<hr/>

<h2>🧪 Rodando os Testes</h2>

<pre><code id="test-commands">cd tests
dotnet add package Moq
dotnet add package Xunit
dotnet restore
dotnet build
dotnet test</code></pre>






