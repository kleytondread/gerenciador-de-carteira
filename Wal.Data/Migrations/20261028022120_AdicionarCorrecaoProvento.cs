using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarCorrecaoProvento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DataPagamentoCorrigida",
                table: "Proventos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Oculto",
                table: "Proventos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TipoCorrigido",
                table: "Proventos",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataPagamentoCorrigida",
                table: "Proventos");

            migrationBuilder.DropColumn(
                name: "Oculto",
                table: "Proventos");

            migrationBuilder.DropColumn(
                name: "TipoCorrigido",
                table: "Proventos");
        }
    }
}
