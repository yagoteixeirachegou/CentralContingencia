# ADR 0001 — Fundação do monólito modular

Status: aceita para Etapa 0.

ASP.NET Core 10 e React 19 com TypeScript/Vite compõem um monólito modular
com DDD pragmático e Clean Architecture. Domain não possui dependências externas;
Application referencia Domain; Infrastructure referencia Application/Domain;
Api referencia Application/Infrastructure, esta última apenas no composition root.

Os contextos Proxies, Profiles, Allocations, Identity e Audit serão criados conforme
as respectivas fatias forem autorizadas. Não há entidades ou casos de uso nesta etapa.

PostgreSQL 17 é executado somente em desenvolvimento via Docker Compose, com volume
persistente, healthcheck e porta restrita ao loopback. EF Core 10/Npgsql estão na
Infrastructure; o DbContext não tem entidades, não é registrado na API e não aplica migrations.

A API expõe apenas health do processo e OpenAPI em Development. A interface usa
Tailwind, Lucide e a convenção de componentes locais do shadcn/ui. Bibliotecas de
rotas, cache, tabelas e formulários serão adicionadas quando houver usos concretos.

Testes xUnit verificam limites das camadas e comportamento HTTP. O teste de PostgreSQL
real é habilitado por CENTRAL_TEST_POSTGRES e deve ser apontado exclusivamente para
o banco local de desenvolvimento. Vitest/Testing Library verificam a tela inicial.

Consequências: estrutura pronta para fatias incrementais, sem abstrações de negócio
prematuras. Sem autenticação, CRUD, migrations, dados reais ou serviços externos.
