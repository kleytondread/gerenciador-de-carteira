using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarEventoCorporativo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventosCorporativos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtivoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Numerador = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Denominador = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Origem = table.Column<int>(type: "INTEGER", nullable: false),
                    DataImportacao = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosCorporativos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosCorporativos_Ativos_AtivoId",
                        column: x => x.AtivoId,
                        principalTable: "Ativos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosCorporativos_AtivoId_Data_Origem",
                table: "EventosCorporativos",
                columns: new[] { "AtivoId", "Data", "Origem" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosCorporativos");
        }
    }
}
