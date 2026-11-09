using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalTarefas.Web.Dados.Migrations
{
    /// <inheritdoc />
    public partial class IndiceSituacaoPrazo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_Situacao_Prazo",
                table: "Tarefas",
                columns: new[] { "Situacao", "Prazo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tarefas_Situacao_Prazo",
                table: "Tarefas");
        }
    }
}
