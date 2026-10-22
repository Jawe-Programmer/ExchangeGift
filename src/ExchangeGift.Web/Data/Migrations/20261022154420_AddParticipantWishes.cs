using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExchangeGift.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantWishes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Wishes",
                table: "ExchangeGiftParticipants",
                type: "TEXT",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Wishes",
                table: "ExchangeGiftParticipants");
        }
    }
}
