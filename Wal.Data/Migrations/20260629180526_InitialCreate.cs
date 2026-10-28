using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wal.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ativos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ticker = table.Column<string>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    EstaAtivo = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ativos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CotacoesHistoricas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtivoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Abertura = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Maxima = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Minima = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Fechamento = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotacoesHistoricas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CotacoesHistoricas_Ativos_AtivoId",
                        column: x => x.AtivoId,
                        principalTable: "Ativos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Movimentacoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtivoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Quantidade = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Taxas = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movimentacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movimentacoes_Ativos_AtivoId",
                        column: x => x.AtivoId,
                        principalTable: "Ativos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Proventos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtivoId = table.Column<int>(type: "INTEGER", nullable: false),
                    TipoProvento = table.Column<int>(type: "INTEGER", nullable: false),
                    DataCom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DataPagamento = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValorPorAcao = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    DataImportacao = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Proventos_Ativos_AtivoId",
                        column: x => x.AtivoId,
                        principalTable: "Ativos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ativos_Ticker",
                table: "Ativos",
                column: "Ticker",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CotacoesHistoricas_AtivoId_Data",
                table: "CotacoesHistoricas",
                columns: new[] { "AtivoId", "Data" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movimentacoes_AtivoId",
                table: "Movimentacoes",
                column: "AtivoId");

            migrationBuilder.CreateIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_TipoProvento",
                table: "Proventos",
                columns: new[] { "AtivoId", "DataPagamento", "TipoProvento" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CotacoesHistoricas");

            migrationBuilder.DropTable(
                name: "Movimentacoes");

            migrationBuilder.DropTable(
                name: "Proventos");

            migrationBuilder.DropTable(
                name: "Ativos");
        }
    }
}
