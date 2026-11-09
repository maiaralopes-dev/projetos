BEGIN TRANSACTION;
DROP INDEX "IX_Pedidos_Cliente_CriadoEm";
DROP INDEX "IX_Pedidos_Status_CriadoEm";
CREATE INDEX "IX_Pedidos_Cliente_CriadoEm_Id" ON "Pedidos" ("ClienteId", "CriadoEmUtc", "Id");
CREATE INDEX "IX_Pedidos_Status_CriadoEm_Id" ON "Pedidos" ("Status", "CriadoEmUtc", "Id");
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261109012001_IndicesComDesempate', '10.0.14');
COMMIT;
