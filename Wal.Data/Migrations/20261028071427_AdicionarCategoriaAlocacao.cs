using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarCategoriaAlocacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Categoria",
                table: "Ativos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Sugestão pelo tipo (CategoriasAlocacao.Padrao): BDR em dólar, Tesouro em renda fixa, o resto Brasil.
            // ETFs de S&P 500 ou de títulos são ajustados à mão na tela de Ativos rastreados.
            migrationBuilder.Sql("UPDATE Ativos SET Categoria = 1 WHERE Tipo = 4");
            migrationBuilder.Sql("UPDATE Ativos SET Categoria = 2 WHERE Tipo = 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Categoria",
                table: "Ativos");
        }
    }
}
