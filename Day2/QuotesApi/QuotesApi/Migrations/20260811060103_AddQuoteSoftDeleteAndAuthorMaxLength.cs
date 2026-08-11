using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotesApi.Migrations;

public partial class AddQuoteSoftDeleteAndAuthorMaxLength : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Author",
            table: "Quotes",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 100,
            oldNullable: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "Quotes",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "Quotes");

        migrationBuilder.AlterColumn<string>(
            name: "Author",
            table: "Quotes",
            type: "TEXT",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldNullable: false);
    }
}
