Build started...
Build succeeded.
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Pedidos" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Pedidos" PRIMARY KEY,
    "ClienteId" TEXT NOT NULL,
    "Regiao" INTEGER NOT NULL,
    "Prioridade" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "Categoria" INTEGER NOT NULL,
    "TotalCentavos" INTEGER NOT NULL,
    "CriadoEmUtc" TEXT NOT NULL,
    "Entrega_Cep" TEXT NULL,
    "Entrega_Cidade" TEXT NULL,
    "Entrega_Logradouro" TEXT NULL,
    "Entrega_Uf" TEXT NULL,
    "Metadados" TEXT NOT NULL
);

CREATE TABLE "ItensPedido" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ItensPedido" PRIMARY KEY AUTOINCREMENT,
    "PedidoId" TEXT NOT NULL,
    "Sku" TEXT NOT NULL,
    "Quantidade" INTEGER NOT NULL,
    "PrecoUnitarioCentavos" INTEGER NOT NULL,
    CONSTRAINT "FK_ItensPedido_Pedidos_PedidoId" FOREIGN KEY ("PedidoId") REFERENCES "Pedidos" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "UX_ItensPedido_Pedido_Sku" ON "ItensPedido" ("PedidoId", "Sku");

CREATE INDEX "IX_Pedidos_Cliente_CriadoEm" ON "Pedidos" ("ClienteId", "CriadoEmUtc");

CREATE INDEX "IX_Pedidos_CriadoEm_Id" ON "Pedidos" ("CriadoEmUtc", "Id");

CREATE INDEX "IX_Pedidos_Regiao" ON "Pedidos" ("Regiao");

CREATE INDEX "IX_Pedidos_Status_CriadoEm" ON "Pedidos" ("Status", "CriadoEmUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261109010732_Inicial', '10.0.14');

COMMIT;


