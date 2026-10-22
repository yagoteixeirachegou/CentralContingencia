START TRANSACTION;
ALTER TABLE profiles."OperationalProfiles" ALTER COLUMN "Platform" DROP NOT NULL;

ALTER TABLE profiles."OperationalProfiles" ADD "Platform1" character varying(100);

ALTER TABLE profiles."OperationalProfiles" ADD "Platform2" character varying(100);

ALTER TABLE profiles."OperationalProfiles" ADD CONSTRAINT "CK_Profiles_Platform1" CHECK ("Platform1" IS NULL OR "Platform1" IN ('DARKPOST','MODERAÇÃO','ANUNCIANTE','HOST PÁGINAS','FLAG','BACKUP PIXEL','ESTOURADO','RESTRITA','BUGADO','ATIVO (LIVRE)','ESTEIRA DE PERFIL','MINERAÇÃO','FLAG (LIVRE)','TIKTOK','BM','X1','GOOGLE ADS'));

ALTER TABLE profiles."OperationalProfiles" ADD CONSTRAINT "CK_Profiles_Platform2" CHECK ("Platform2" IS NULL OR "Platform2" IN ('DARKPOST','MODERAÇÃO','ANUNCIANTE','HOST PÁGINAS','FLAG','BACKUP PIXEL','ESTOURADO','RESTRITA','BUGADO','ATIVO (LIVRE)','ESTEIRA DE PERFIL','MINERAÇÃO','FLAG (LIVRE)','TIKTOK','BM','X1','GOOGLE ADS'));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012102737_AddProfileAssignments', '10.0.8');

COMMIT;

