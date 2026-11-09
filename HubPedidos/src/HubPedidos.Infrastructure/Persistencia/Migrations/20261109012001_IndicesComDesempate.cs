using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HubPedidos.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class IndicesComDesempate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pedidos_Cliente_CriadoEm",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_Status_CriadoEm",
                table: "Pedidos");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Cliente_CriadoEm_Id",
                table: "Pedidos",
                columns: new[] { "ClienteId", "CriadoEmUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Status_CriadoEm_Id",
                table: "Pedidos",
                columns: new[] { "Status", "CriadoEmUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pedidos_Cliente_CriadoEm_Id",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_Status_CriadoEm_Id",
                table: "Pedidos");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Cliente_CriadoEm",
                table: "Pedidos",
                columns: new[] { "ClienteId", "CriadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Status_CriadoEm",
                table: "Pedidos",
                columns: new[] { "Status", "CriadoEmUtc" });
        }
    }
}
