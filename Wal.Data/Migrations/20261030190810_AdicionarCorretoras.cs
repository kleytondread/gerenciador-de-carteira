using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarCorretoras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CorretoraId",
                table: "Ativos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Corretoras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    Cnpj = table.Column<string>(type: "TEXT", nullable: false),
                    Principal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Corretoras", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ativos_CorretoraId",
                table: "Ativos",
                column: "CorretoraId");

            migrationBuilder.CreateIndex(
                name: "IX_Corretoras_Cnpj",
                table: "Corretoras",
                column: "Cnpj",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Ativos_Corretoras_CorretoraId",
                table: "Ativos",
                column: "CorretoraId",
                principalTable: "Corretoras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ativos_Corretoras_CorretoraId",
                table: "Ativos");

            migrationBuilder.DropTable(
                name: "Corretoras");

            migrationBuilder.DropIndex(
                name: "IX_Ativos_CorretoraId",
                table: "Ativos");

            migrationBuilder.DropColumn(
                name: "CorretoraId",
                table: "Ativos");
        }
    }
}
