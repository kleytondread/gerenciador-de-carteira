using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlterarIndiceProvento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_TipoProvento",
                table: "Proventos");

            migrationBuilder.CreateIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_Origem",
                table: "Proventos",
                columns: new[] { "AtivoId", "DataPagamento", "Origem" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_Origem",
                table: "Proventos");

            migrationBuilder.CreateIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_TipoProvento",
                table: "Proventos",
                columns: new[] { "AtivoId", "DataPagamento", "TipoProvento" },
                unique: true);
        }
    }
}
