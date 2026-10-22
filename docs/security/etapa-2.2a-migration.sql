START TRANSACTION;
CREATE TABLE audit."ProxyEvents" (
    "Id" uuid NOT NULL,
    "ActorId" uuid NOT NULL,
    "ProxyId" uuid NOT NULL,
    "Action" character varying(16) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "CorrelationId" character varying(128) NOT NULL,
    CONSTRAINT "PK_ProxyEvents" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_ProxyEvents_ActorId" ON audit."ProxyEvents" ("ActorId");

CREATE INDEX "IX_ProxyEvents_ProxyId_OccurredAt" ON audit."ProxyEvents" ("ProxyId", "OccurredAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011025452_AddProxyAudit', '10.0.8');

COMMIT;

