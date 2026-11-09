using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HubPedidos.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pedidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClienteId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Regiao = table.Column<int>(type: "INTEGER", nullable: false),
                    Prioridade = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Categoria = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Entrega_Cep = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Entrega_Cidade = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Entrega_Logradouro = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Entrega_Uf = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    Metadados = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pedidos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItensPedido",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PedidoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Quantidade = table.Column<int>(type: "INTEGER", nullable: false),
                    PrecoUnitarioCentavos = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensPedido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensPedido_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ItensPedido_Pedido_Sku",
                table: "ItensPedido",
                columns: new[] { "PedidoId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Cliente_CriadoEm",
                table: "Pedidos",
                columns: new[] { "ClienteId", "CriadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CriadoEm_Id",
                table: "Pedidos",
                columns: new[] { "CriadoEmUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Regiao",
                table: "Pedidos",
                column: "Regiao");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Status_CriadoEm",
                table: "Pedidos",
                columns: new[] { "Status", "CriadoEmUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensPedido");

            migrationBuilder.DropTable(
                name: "Pedidos");
        }
    }
}
