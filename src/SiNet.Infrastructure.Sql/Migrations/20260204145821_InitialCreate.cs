using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiNetSQL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.CreateTable(
            //    name: "Bank_Projects",
            //    columns: table => new
            //    {
            //        BankID = table.Column<int>(type: "int", nullable: false),
            //        ProjectsID = table.Column<int>(type: "int", nullable: false),
            //        Present = table.Column<decimal>(type: "numeric(10,1)", nullable: false, defaultValue: 1.0m)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("Bank_Projects_Index", x => new { x.BankID, x.ProjectsID });
            //    });

            //migrationBuilder.CreateTable(
            //    name: "ProjectAssignment_ProjectAssignment",
            //    columns: table => new
            //    {
            //        ProjectAssignmentID = table.Column<int>(type: "int", nullable: false),
            //        ProjectAssignmentID1 = table.Column<int>(type: "int", nullable: false)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("ProjectAssignment_ProjectAssignment_Index", x => new { x.ProjectAssignmentID, x.ProjectAssignmentID1 });
            //    });

            //migrationBuilder.CreateTable(
            //    name: "SIUser",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        sid = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Name = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        LoginName = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Email = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Notes = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        IsDomainGroup = table.Column<bool>(type: "bit", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_SIUser", x => x.ID);
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Announcements",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true),
            //        Body = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Expires = table.Column<DateTime>(type: "datetime", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Announcements", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Announcements_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Announcements_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Company",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        JobTitle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkPhone = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        CellPhone = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkFax = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkAddress = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkCity = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkState = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkZip = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkCountry = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WebPage = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Comments = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        NotCompany = table.Column<bool>(type: "bit", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Company", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Company_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Company_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "jobTitle",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_jobTitle", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_jobTitle_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_jobTitle_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "JobType",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_JobType", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_JobType_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_JobType_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "LayerObjectType",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_LayerObjectType", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_LayerObjectType_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_LayerObjectType_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "MavatBlock",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Layer = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_MavatBlock", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_MavatBlock_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_MavatBlock_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Mifrat",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        LayerName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Mifrat", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Mifrat_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Mifrat_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Place",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        CityIcon = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        InUse = table.Column<bool>(type: "bit", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Place", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Place_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Place_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "ProjectFolder",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        INFOLDERID = table.Column<int>(type: "int", nullable: true),
            //        SecurityLevel = table.Column<float>(type: "real", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_ProjectFolder", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFolder_ProjectFolder_INFOLDER",
            //            column: x => x.INFOLDERID,
            //            principalTable: "ProjectFolder",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFolder_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFolder_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "ProjectStatus",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_ProjectStatus", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectStatus_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectStatus_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "ServiceProviders",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_ServiceProviders", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_ServiceProviders_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ServiceProviders_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "TabaData",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Seyf = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Grop = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Code = table.Column<float>(type: "real", nullable: true),
            //        LayerColorA = table.Column<float>(type: "real", nullable: true),
            //        LayerColorB = table.Column<float>(type: "real", nullable: true),
            //        LayerColorBB = table.Column<float>(type: "real", nullable: true),
            //        LayerColorBA = table.Column<float>(type: "real", nullable: true),
            //        LayerColorAA = table.Column<float>(type: "real", nullable: true),
            //        TavnitType = table.Column<float>(type: "real", nullable: true),
            //        TavnitType2 = table.Column<float>(type: "real", nullable: true),
            //        ToRemove = table.Column<bool>(type: "bit", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_TabaData", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_TabaData_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_TabaData_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Bank",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Date = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Page = table.Column<float>(type: "real", nullable: true),
            //        Ref = table.Column<float>(type: "real", nullable: true),
            //        Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Mandatory = table.Column<decimal>(type: "money", nullable: true, defaultValue: 0m),
            //        Rights = table.Column<decimal>(type: "money", nullable: true, defaultValue: 0m),
            //        Balance = table.Column<decimal>(type: "money", nullable: true, defaultValue: 0m),
            //        AccountNumber = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        DescriptionDuty = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        OldProject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        PayFromID = table.Column<int>(type: "int", nullable: true),
            //        PayToID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true),
            //        DescriptionBank = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Bank", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Bank_Company_PayFrom",
            //            column: x => x.PayFromID,
            //            principalTable: "Company",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Bank_Company_PayTo",
            //            column: x => x.PayToID,
            //            principalTable: "Company",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Bank_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Bank_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Contacts",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        FirstName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        FullName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        JobTitleID = table.Column<int>(type: "int", nullable: true),
            //        WorkPhone = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        CompanyID = table.Column<int>(type: "int", nullable: true),
            //        HomePhone = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        CellPhone = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkFax = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkAddress = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkCity = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkState = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkZip = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkCountry = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WebPage = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Comments = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Status = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Contacts", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Contacts_Company_Company",
            //            column: x => x.CompanyID,
            //            principalTable: "Company",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Contacts_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Contacts_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Contacts_jobTitle_JobTitle",
            //            column: x => x.JobTitleID,
            //            principalTable: "jobTitle",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Layers",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        LayerName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        LayerObjectTypeID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Layers", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Layers_LayerObjectType_LayerObjectType",
            //            column: x => x.LayerObjectTypeID,
            //            principalTable: "LayerObjectType",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Layers_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Layers_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "DrawingType",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        MifratID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_DrawingType", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_DrawingType_Mifrat_Mifrat",
            //            column: x => x.MifratID,
            //            principalTable: "Mifrat",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_DrawingType_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_DrawingType_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "ProjectFile",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Number = table.Column<float>(type: "real", nullable: true),
            //        des = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        FOLDERID = table.Column<int>(type: "int", nullable: true),
            //        TYPEFILE = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        TypeProjID = table.Column<int>(type: "int", nullable: true),
            //        TemplateLocation = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        LookAtDes = table.Column<bool>(type: "bit", nullable: true),
            //        OutSidData = table.Column<bool>(type: "bit", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_ProjectFile", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFile_JobType_TypeProj",
            //            column: x => x.TypeProjID,
            //            principalTable: "JobType",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFile_ProjectFolder_FOLDER",
            //            column: x => x.FOLDERID,
            //            principalTable: "ProjectFolder",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFile_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFile_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Projects",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(46)", maxLength: 46, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Number = table.Column<float>(type: "real", nullable: true),
            //        CompanyID = table.Column<int>(type: "int", nullable: true),
            //        worker = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Start = table.Column<DateTime>(type: "datetime", nullable: true),
            //        End = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Admin = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        PlaceID = table.Column<int>(type: "int", nullable: true),
            //        ProjectPath = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        EndOfProject = table.Column<bool>(type: "bit", nullable: true),
            //        NameAndNumber = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        OnerProjectID = table.Column<int>(type: "int", nullable: true),
            //        MazcirotTik = table.Column<float>(type: "real", nullable: true),
            //        ProjectStatusID = table.Column<int>(type: "int", nullable: true),
            //        PriceQuoteDescription = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        ContactsID = table.Column<int>(type: "int", nullable: true),
            //        ApproveDescription = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        ApproveDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Projects", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Projects_Company_Company",
            //            column: x => x.CompanyID,
            //            principalTable: "Company",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Projects_Contacts_Contacts",
            //            column: x => x.ContactsID,
            //            principalTable: "Contacts",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Projects_Place_Place",
            //            column: x => x.PlaceID,
            //            principalTable: "Place",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Projects_ProjectStatus_ProjectStatus",
            //            column: x => x.ProjectStatusID,
            //            principalTable: "ProjectStatus",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Projects_Projects_OnerProject",
            //            column: x => x.OnerProjectID,
            //            principalTable: "Projects",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Projects_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Projects_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Properties",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Color = table.Column<float>(type: "real", nullable: true),
            //        Linetype = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Lineweight = table.Column<float>(type: "real", nullable: true),
            //        Plottable = table.Column<bool>(type: "bit", nullable: true),
            //        PlotStyleName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        ViewportDefault = table.Column<bool>(type: "bit", nullable: true),
            //        Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Description_x0020_HE = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        MifratID = table.Column<int>(type: "int", nullable: true),
            //        LayersID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Properties", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Properties_Layers_Layers",
            //            column: x => x.LayersID,
            //            principalTable: "Layers",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Properties_Mifrat_Mifrat",
            //            column: x => x.MifratID,
            //            principalTable: "Mifrat",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Properties_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Properties_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "DrawingTypeAndLayersTable",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        GropNameID = table.Column<int>(type: "int", nullable: true),
            //        ObjectsNameID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_DrawingTypeAndLayersTable", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_DrawingTypeAndLayersTable_DrawingType_GropName",
            //            column: x => x.GropNameID,
            //            principalTable: "DrawingType",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_DrawingTypeAndLayersTable_Layers_ObjectsName",
            //            column: x => x.ObjectsNameID,
            //            principalTable: "Layers",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_DrawingTypeAndLayersTable_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_DrawingTypeAndLayersTable_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "ProjectFileRef",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        XRefID = table.Column<int>(type: "int", nullable: true),
            //        FileID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_ProjectFileRef", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFileRef_ProjectFile_File",
            //            column: x => x.FileID,
            //            principalTable: "ProjectFile",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFileRef_ProjectFile_XRef",
            //            column: x => x.XRefID,
            //            principalTable: "ProjectFile",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFileRef_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectFileRef_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Bid",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        ProjectsID = table.Column<int>(type: "int", nullable: false),
            //        JobTypeID = table.Column<int>(type: "int", nullable: false),
            //        BidValue = table.Column<decimal>(type: "money", nullable: false),
            //        BidSubmission = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
            //        VAT = table.Column<decimal>(type: "numeric(3,3)", nullable: false, defaultValue: 0.18m),
            //        Description = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false, collation: "Hebrew_100_CI_AS")
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Bid", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Bid_JobType",
            //            column: x => x.JobTypeID,
            //            principalTable: "JobType",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_Bid_Projects",
            //            column: x => x.ProjectsID,
            //            principalTable: "Projects",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "PaymentsStep",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        StepNumber = table.Column<float>(type: "real", nullable: true),
            //        ProjectID = table.Column<int>(type: "int", nullable: true),
            //        ContractValue = table.Column<decimal>(type: "money", nullable: true),
            //        JobTypeID = table.Column<int>(type: "int", nullable: true),
            //        Percent = table.Column<float>(type: "real", nullable: true),
            //        ExpectedStepPayment = table.Column<decimal>(type: "money", nullable: true),
            //        ExpectedPaymentDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        BillSubmission = table.Column<DateTime>(type: "datetime", nullable: true),
            //        BillApproval = table.Column<DateTime>(type: "datetime", nullable: true),
            //        ApprovalPercent = table.Column<float>(type: "real", nullable: true),
            //        ApprovalStepPayment = table.Column<decimal>(type: "money", nullable: true),
            //        invoice = table.Column<DateTime>(type: "datetime", nullable: true),
            //        PaymentDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        StepPayment = table.Column<decimal>(type: "money", nullable: true),
            //        bankID = table.Column<int>(type: "int", nullable: true),
            //        Description = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        ContractDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        VAT = table.Column<float>(type: "real", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_PaymentsStep", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_PaymentsStep_Bank_bank",
            //            column: x => x.bankID,
            //            principalTable: "Bank",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_PaymentsStep_JobType_JobType",
            //            column: x => x.JobTypeID,
            //            principalTable: "JobType",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_PaymentsStep_Projects_Project",
            //            column: x => x.ProjectID,
            //            principalTable: "Projects",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_PaymentsStep_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_PaymentsStep_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "ProjectAssignment",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        ProjectID = table.Column<int>(type: "int", nullable: true),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Priority = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Status = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        PercentComplete = table.Column<float>(type: "real", nullable: true),
            //        AssignedToID = table.Column<int>(type: "int", nullable: true),
            //        Body = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        StartDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        DueDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        grading = table.Column<float>(type: "real", nullable: true),
            //        TaskGroupID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_ProjectAssignment", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectAssignment_Projects_Project",
            //            column: x => x.ProjectID,
            //            principalTable: "Projects",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectAssignment_User_AssignedTo",
            //            column: x => x.AssignedToID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectAssignment_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectAssignment_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectAssignment_User_TaskGroup",
            //            column: x => x.TaskGroupID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "ProjectPlanner",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        ContactsID = table.Column<int>(type: "int", nullable: true),
            //        projctID = table.Column<int>(type: "int", nullable: true),
            //        RoleID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_ProjectPlanner", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectPlanner_Contacts_Contacts",
            //            column: x => x.ContactsID,
            //            principalTable: "Contacts",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectPlanner_Projects_projct",
            //            column: x => x.projctID,
            //            principalTable: "Projects",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectPlanner_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectPlanner_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_ProjectPlanner_jobTitle_Role",
            //            column: x => x.RoleID,
            //            principalTable: "jobTitle",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "TypeOfProjectInProject",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        ProjectTypeID = table.Column<int>(type: "int", nullable: true),
            //        ProjectID = table.Column<int>(type: "int", nullable: true),
            //        AdminWorkerID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_TypeOfProjectInProject", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_TypeOfProjectInProject_JobType_ProjectType",
            //            column: x => x.ProjectTypeID,
            //            principalTable: "JobType",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_TypeOfProjectInProject_Projects_Project",
            //            column: x => x.ProjectID,
            //            principalTable: "Projects",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_TypeOfProjectInProject_User_AdminWorker",
            //            column: x => x.AdminWorkerID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_TypeOfProjectInProject_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_TypeOfProjectInProject_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "WeekWork",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        ProjectID = table.Column<int>(type: "int", nullable: true),
            //        WorkHours = table.Column<float>(type: "real", nullable: true),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Week = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Priority = table.Column<float>(type: "real", nullable: true),
            //        JobStatus = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        workerID = table.Column<int>(type: "int", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_WeekWork", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_WeekWork_Projects_Project",
            //            column: x => x.ProjectID,
            //            principalTable: "Projects",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_WeekWork_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_WeekWork_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_WeekWork_User_worker",
            //            column: x => x.workerID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "WorkHour",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        ProjectID = table.Column<int>(type: "int", nullable: true),
            //        EventDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        EndDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Description = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        fRecurrence = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        WorkspaceLink = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        fAllDayEvent = table.Column<bool>(type: "bit", nullable: true),
            //        ParticipantsPicker = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Category = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Facilities = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        FreeBusy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        Overbook = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        payByHomer = table.Column<bool>(type: "bit", nullable: true),
            //        Payd = table.Column<bool>(type: "bit", nullable: true),
            //        SendToPay = table.Column<bool>(type: "bit", nullable: true),
            //        Modified = table.Column<DateTime>(type: "datetime", nullable: true),
            //        Created = table.Column<DateTime>(type: "datetime", nullable: true),
            //        AuthorID = table.Column<int>(type: "int", nullable: true),
            //        EditorID = table.Column<int>(type: "int", nullable: true)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_WorkHour", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_WorkHour_Projects_Project",
            //            column: x => x.ProjectID,
            //            principalTable: "Projects",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_WorkHour_User_Author",
            //            column: x => x.AuthorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //        table.ForeignKey(
            //            name: "FK_SI_WorkHour_User_Editor",
            //            column: x => x.EditorID,
            //            principalTable: "SIUser",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Contract",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        BidID = table.Column<int>(type: "int", nullable: false),
            //        ContractValue = table.Column<decimal>(type: "money", nullable: false),
            //        ContractApproval = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
            //        VAT = table.Column<decimal>(type: "numeric(3,3)", nullable: false, defaultValue: 0.18m),
            //        Description = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false, collation: "Hebrew_100_CI_AS")
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Contract", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Contract_Bid",
            //            column: x => x.BidID,
            //            principalTable: "Bid",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateTable(
            //    name: "Bill",
            //    columns: table => new
            //    {
            //        ID = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        ContractID = table.Column<int>(type: "int", nullable: false),
            //        Description = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true, collation: "Hebrew_100_CI_AS"),
            //        BillValue = table.Column<decimal>(type: "money", nullable: false),
            //        BillSubmission = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
            //        ApprovValue = table.Column<decimal>(type: "money", nullable: true),
            //        BillApproval = table.Column<DateTime>(type: "datetime", nullable: true),
            //        invoice = table.Column<DateTime>(type: "datetime", nullable: true),
            //        PaymentValue = table.Column<decimal>(type: "money", nullable: true),
            //        PaymentDate = table.Column<DateTime>(type: "datetime", nullable: true),
            //        VAT = table.Column<decimal>(type: "numeric(3,3)", nullable: false, defaultValue: 0.18m)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Bill", x => x.ID);
            //        table.ForeignKey(
            //            name: "FK_SI_Bill_Contract",
            //            column: x => x.ContractID,
            //            principalTable: "Contract",
            //            principalColumn: "ID");
            //    });

            //migrationBuilder.CreateIndex(
            //    name: "Announcements_TitleIndex",
            //    table: "Announcements",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Announcements_AuthorID",
            //    table: "Announcements",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Announcements_EditorID",
            //    table: "Announcements",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Bank",
            //    table: "Bank",
            //    columns: new[] { "Date", "Description", "Mandatory", "Rights", "Balance", "Ref" },
            //    unique: true,
            //    filter: "[Date] IS NOT NULL AND [Description] IS NOT NULL AND [Mandatory] IS NOT NULL AND [Rights] IS NOT NULL AND [Balance] IS NOT NULL AND [Ref] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Bank_AuthorID",
            //    table: "Bank",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Bank_EditorID",
            //    table: "Bank",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Bank_PayFromID",
            //    table: "Bank",
            //    column: "PayFromID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Bank_PayToID",
            //    table: "Bank",
            //    column: "PayToID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Bid",
            //    table: "Bid",
            //    columns: new[] { "ProjectsID", "JobTypeID" },
            //    unique: true);

            //migrationBuilder.CreateIndex(
            //    name: "IX_Bid_JobTypeID",
            //    table: "Bid",
            //    column: "JobTypeID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Bill_ContractID",
            //    table: "Bill",
            //    column: "ContractID");

            //migrationBuilder.CreateIndex(
            //    name: "Company_TitleIndex",
            //    table: "Company",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Company_AuthorID",
            //    table: "Company",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Company_EditorID",
            //    table: "Company",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Contacts_AuthorID",
            //    table: "Contacts",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Contacts_CompanyID",
            //    table: "Contacts",
            //    column: "CompanyID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Contacts_EditorID",
            //    table: "Contacts",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Contacts_JobTitleID",
            //    table: "Contacts",
            //    column: "JobTitleID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Contract_BidID",
            //    table: "Contract",
            //    column: "BidID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_DrawingType_AuthorID",
            //    table: "DrawingType",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_DrawingType_EditorID",
            //    table: "DrawingType",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_DrawingType_MifratID",
            //    table: "DrawingType",
            //    column: "MifratID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_DrawingTypeAndLayersTable_AuthorID",
            //    table: "DrawingTypeAndLayersTable",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_DrawingTypeAndLayersTable_EditorID",
            //    table: "DrawingTypeAndLayersTable",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_DrawingTypeAndLayersTable_GropNameID",
            //    table: "DrawingTypeAndLayersTable",
            //    column: "GropNameID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_DrawingTypeAndLayersTable_ObjectsNameID",
            //    table: "DrawingTypeAndLayersTable",
            //    column: "ObjectsNameID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_jobTitle_AuthorID",
            //    table: "jobTitle",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_jobTitle_EditorID",
            //    table: "jobTitle",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "jobTitle_TitleIndex",
            //    table: "jobTitle",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_JobType_AuthorID",
            //    table: "JobType",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_JobType_EditorID",
            //    table: "JobType",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "JobType_TitleIndex",
            //    table: "JobType",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_LayerObjectType_AuthorID",
            //    table: "LayerObjectType",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_LayerObjectType_EditorID",
            //    table: "LayerObjectType",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "LayerObjectType_TitleIndex",
            //    table: "LayerObjectType",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Layers_AuthorID",
            //    table: "Layers",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Layers_EditorID",
            //    table: "Layers",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Layers_LayerObjectTypeID",
            //    table: "Layers",
            //    column: "LayerObjectTypeID");

            //migrationBuilder.CreateIndex(
            //    name: "Layers_TitleIndex",
            //    table: "Layers",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_MavatBlock_AuthorID",
            //    table: "MavatBlock",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_MavatBlock_EditorID",
            //    table: "MavatBlock",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "MavatBlock_TitleIndex",
            //    table: "MavatBlock",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Mifrat_AuthorID",
            //    table: "Mifrat",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Mifrat_EditorID",
            //    table: "Mifrat",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "Mifrat_TitleIndex",
            //    table: "Mifrat",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_PaymentsStep_AuthorID",
            //    table: "PaymentsStep",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_PaymentsStep_bankID",
            //    table: "PaymentsStep",
            //    column: "bankID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_PaymentsStep_EditorID",
            //    table: "PaymentsStep",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_PaymentsStep_JobTypeID",
            //    table: "PaymentsStep",
            //    column: "JobTypeID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_PaymentsStep_ProjectID",
            //    table: "PaymentsStep",
            //    column: "ProjectID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Place_AuthorID",
            //    table: "Place",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Place_EditorID",
            //    table: "Place",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "Place_TitleIndex",
            //    table: "Place",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectAssignment_AssignedToID",
            //    table: "ProjectAssignment",
            //    column: "AssignedToID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectAssignment_AuthorID",
            //    table: "ProjectAssignment",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectAssignment_EditorID",
            //    table: "ProjectAssignment",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectAssignment_ProjectID",
            //    table: "ProjectAssignment",
            //    column: "ProjectID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectAssignment_TaskGroupID",
            //    table: "ProjectAssignment",
            //    column: "TaskGroupID");

            //migrationBuilder.CreateIndex(
            //    name: "ProjectAssignment_TitleIndex",
            //    table: "ProjectAssignment",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFile_AuthorID",
            //    table: "ProjectFile",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFile_EditorID",
            //    table: "ProjectFile",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFile_FOLDERID",
            //    table: "ProjectFile",
            //    column: "FOLDERID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFile_TypeProjID",
            //    table: "ProjectFile",
            //    column: "TypeProjID");

            //migrationBuilder.CreateIndex(
            //    name: "uc_Number_TypeProjID",
            //    table: "ProjectFile",
            //    columns: new[] { "Number", "TypeProjID" },
            //    unique: true,
            //    filter: "[Number] IS NOT NULL AND [TypeProjID] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "uc_Title",
            //    table: "ProjectFile",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFileRef_AuthorID",
            //    table: "ProjectFileRef",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFileRef_EditorID",
            //    table: "ProjectFileRef",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFileRef_FileID",
            //    table: "ProjectFileRef",
            //    column: "FileID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFileRef_XRefID",
            //    table: "ProjectFileRef",
            //    column: "XRefID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFolder_AuthorID",
            //    table: "ProjectFolder",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFolder_EditorID",
            //    table: "ProjectFolder",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectFolder_INFOLDERID",
            //    table: "ProjectFolder",
            //    column: "INFOLDERID");

            //migrationBuilder.CreateIndex(
            //    name: "ProjectFolder_TitleIndex",
            //    table: "ProjectFolder",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectPlanner_AuthorID",
            //    table: "ProjectPlanner",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectPlanner_ContactsID",
            //    table: "ProjectPlanner",
            //    column: "ContactsID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectPlanner_EditorID",
            //    table: "ProjectPlanner",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectPlanner_projctID",
            //    table: "ProjectPlanner",
            //    column: "projctID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectPlanner_RoleID",
            //    table: "ProjectPlanner",
            //    column: "RoleID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Projects_AuthorID",
            //    table: "Projects",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Projects_CompanyID",
            //    table: "Projects",
            //    column: "CompanyID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Projects_ContactsID",
            //    table: "Projects",
            //    column: "ContactsID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Projects_EditorID",
            //    table: "Projects",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Projects_OnerProjectID",
            //    table: "Projects",
            //    column: "OnerProjectID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Projects_PlaceID",
            //    table: "Projects",
            //    column: "PlaceID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Projects_ProjectStatusID",
            //    table: "Projects",
            //    column: "ProjectStatusID");

            //migrationBuilder.CreateIndex(
            //    name: "Projects_TitleIndex",
            //    table: "Projects",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectStatus_AuthorID",
            //    table: "ProjectStatus",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ProjectStatus_EditorID",
            //    table: "ProjectStatus",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "ProjectStatus_TitleIndex",
            //    table: "ProjectStatus",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Properties_AuthorID",
            //    table: "Properties",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Properties_EditorID",
            //    table: "Properties",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Properties_LayersID",
            //    table: "Properties",
            //    column: "LayersID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_Properties_MifratID",
            //    table: "Properties",
            //    column: "MifratID");

            //migrationBuilder.CreateIndex(
            //    name: "Properties_TitleIndex",
            //    table: "Properties",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ServiceProviders_AuthorID",
            //    table: "ServiceProviders",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_ServiceProviders_EditorID",
            //    table: "ServiceProviders",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "ServiceProviders_TitleIndex",
            //    table: "ServiceProviders",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_TabaData_AuthorID",
            //    table: "TabaData",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_TabaData_EditorID",
            //    table: "TabaData",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "TabaData_TitleIndex",
            //    table: "TabaData",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_TypeOfProjectInProject_AdminWorkerID",
            //    table: "TypeOfProjectInProject",
            //    column: "AdminWorkerID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_TypeOfProjectInProject_AuthorID",
            //    table: "TypeOfProjectInProject",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_TypeOfProjectInProject_EditorID",
            //    table: "TypeOfProjectInProject",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_TypeOfProjectInProject_ProjectID",
            //    table: "TypeOfProjectInProject",
            //    column: "ProjectID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_TypeOfProjectInProject_ProjectTypeID",
            //    table: "TypeOfProjectInProject",
            //    column: "ProjectTypeID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_WeekWork_AuthorID",
            //    table: "WeekWork",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_WeekWork_EditorID",
            //    table: "WeekWork",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_WeekWork_ProjectID",
            //    table: "WeekWork",
            //    column: "ProjectID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_WeekWork_workerID",
            //    table: "WeekWork",
            //    column: "workerID");

            //migrationBuilder.CreateIndex(
            //    name: "WeekWork_TitleIndex",
            //    table: "WeekWork",
            //    column: "Title",
            //    unique: true,
            //    filter: "[Title] IS NOT NULL");

            //migrationBuilder.CreateIndex(
            //    name: "IX_WorkHour_AuthorID",
            //    table: "WorkHour",
            //    column: "AuthorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_WorkHour_EditorID",
            //    table: "WorkHour",
            //    column: "EditorID");

            //migrationBuilder.CreateIndex(
            //    name: "IX_WorkHour_ProjectID",
            //    table: "WorkHour",
            //    column: "ProjectID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.DropTable(
            //    name: "Announcements");

            //migrationBuilder.DropTable(
            //    name: "Bank_Projects");

            //migrationBuilder.DropTable(
            //    name: "Bill");

            //migrationBuilder.DropTable(
            //    name: "DrawingTypeAndLayersTable");

            //migrationBuilder.DropTable(
            //    name: "MavatBlock");

            //migrationBuilder.DropTable(
            //    name: "PaymentsStep");

            //migrationBuilder.DropTable(
            //    name: "ProjectAssignment");

            //migrationBuilder.DropTable(
            //    name: "ProjectAssignment_ProjectAssignment");

            //migrationBuilder.DropTable(
            //    name: "ProjectFileRef");

            //migrationBuilder.DropTable(
            //    name: "ProjectPlanner");

            //migrationBuilder.DropTable(
            //    name: "Properties");

            //migrationBuilder.DropTable(
            //    name: "ServiceProviders");

            //migrationBuilder.DropTable(
            //    name: "TabaData");

            //migrationBuilder.DropTable(
            //    name: "TypeOfProjectInProject");

            //migrationBuilder.DropTable(
            //    name: "WeekWork");

            //migrationBuilder.DropTable(
            //    name: "WorkHour");

            //migrationBuilder.DropTable(
            //    name: "Contract");

            //migrationBuilder.DropTable(
            //    name: "DrawingType");

            //migrationBuilder.DropTable(
            //    name: "Bank");

            //migrationBuilder.DropTable(
            //    name: "ProjectFile");

            //migrationBuilder.DropTable(
            //    name: "Layers");

            //migrationBuilder.DropTable(
            //    name: "Bid");

            //migrationBuilder.DropTable(
            //    name: "Mifrat");

            //migrationBuilder.DropTable(
            //    name: "ProjectFolder");

            //migrationBuilder.DropTable(
            //    name: "LayerObjectType");

            //migrationBuilder.DropTable(
            //    name: "JobType");

            //migrationBuilder.DropTable(
            //    name: "Projects");

            //migrationBuilder.DropTable(
            //    name: "Contacts");

            //migrationBuilder.DropTable(
            //    name: "Place");

            //migrationBuilder.DropTable(
            //    name: "ProjectStatus");

            //migrationBuilder.DropTable(
            //    name: "Company");

            //migrationBuilder.DropTable(
            //    name: "jobTitle");

            //migrationBuilder.DropTable(
            //    name: "SIUser");
        }
    }
}
