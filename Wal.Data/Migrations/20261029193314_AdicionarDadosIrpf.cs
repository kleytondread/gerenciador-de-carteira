using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarDadosIrpf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_TipoProvento_Origem",
                table: "Proventos");

            migrationBuilder.AddColumn<string>(
                name: "Cnpj",
                table: "Ativos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DarfsPagos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ano = table.Column<int>(type: "INTEGER", nullable: false),
                    Mes = table.Column<int>(type: "INTEGER", nullable: false),
                    Ficha = table.Column<int>(type: "INTEGER", nullable: false),
                    ValorPrincipal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DataPagamento = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DarfsPagos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PagamentosProventos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProventoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Quantidade = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ValorBruto = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ValorLiquido = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagamentosProventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagamentosProventos_Proventos_ProventoId",
                        column: x => x.ProventoId,
                        principalTable: "Proventos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_TipoProvento_Origem_DataCom",
                table: "Proventos",
                columns: new[] { "AtivoId", "DataPagamento", "TipoProvento", "Origem", "DataCom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DarfsPagos_Ano_Mes",
                table: "DarfsPagos",
                columns: new[] { "Ano", "Mes" });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosProventos_ProventoId_Data",
                table: "PagamentosProventos",
                columns: new[] { "ProventoId", "Data" });

            // CNPJs já informados na declaração do IRPF 2026 (ano-calendário 2025). B3SA3 usa o da matriz:
            // a declaração tinha o de uma filial (/0003-97). Ativos que ainda não existem no banco são ignorados.
            foreach (var (ticker, cnpj) in CnpjsConhecidos)
                migrationBuilder.Sql($"UPDATE Ativos SET Cnpj = '{cnpj}' WHERE Ticker = '{ticker}' AND Cnpj IS NULL;");
        }

        private static readonly (string Ticker, string Cnpj)[] CnpjsConhecidos =
        [
            ("BBAS3", "00000000000191"),
            ("KLBN11", "89637490000145"),
            ("KLBN4", "89637490000145"),
            ("BBSE3", "17344597000194"),
            ("WIZC3", "42278473000103"),
            ("EZTC3", "08312229000173"),
            ("B3SA3", "09346601000125"),
            ("VALE3", "33592510000154"),
            ("ITSA4", "61532644000115"),
            ("EGIE3", "02474103000119"),
            ("VAMO3", "23373000000132"),
            ("MGLU3", "47960950000121"),
            ("NASD11", "35578672000163"),
            ("DIVO11", "13416245000146"),
            ("IVVB11", "19909560000191"),
            ("HASH11", "38314708000190"),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DarfsPagos");

            migrationBuilder.DropTable(
                name: "PagamentosProventos");

            migrationBuilder.DropIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_TipoProvento_Origem_DataCom",
                table: "Proventos");

            migrationBuilder.DropColumn(
                name: "Cnpj",
                table: "Ativos");

            migrationBuilder.CreateIndex(
                name: "IX_Proventos_AtivoId_DataPagamento_TipoProvento_Origem",
                table: "Proventos",
                columns: new[] { "AtivoId", "DataPagamento", "TipoProvento", "Origem" },
                unique: true);
        }
    }
}
