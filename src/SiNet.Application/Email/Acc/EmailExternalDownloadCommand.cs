namespace SiNet.Application.Email.Acc;

public sealed record EmailExternalDownloadCommand(
    string GmailMessageId,
    string? InternetMessageId,
    string LocalFilePath,
    string FileName,
    string? EmailSubject,
    string? EmailFrom,
    DateTime? EmailDate,
    string ActingUserLogin);
