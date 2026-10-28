using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarIndicesReferencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CotacoesIndices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Indice = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Valor = table.Column<decimal>(type: "TEXT", precision: 20, scale: 10, nullable: false),
                    DataImportacao = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotacoesIndices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CotacoesIndices_Indice_Data",
                table: "CotacoesIndices",
                columns: new[] { "Indice", "Data" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CotacoesIndices");
        }
    }
}
