START TRANSACTION;
ALTER TABLE work."Tasks" ADD "ClaimKey" uuid;

CREATE INDEX "IX_Tasks_Claim" ON work."Tasks" ("WorkerId", "ClaimKey") WHERE "Status" = 'Running' AND "ClaimKey" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021230549_AddClaimKey', '10.0.8');

COMMIT;

