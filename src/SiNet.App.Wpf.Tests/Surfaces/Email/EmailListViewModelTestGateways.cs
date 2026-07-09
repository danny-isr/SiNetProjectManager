using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

internal static partial class EmailListViewModelTestFixtures
{
    private const string SampleFiledProjectLabelPath = EmailGmailLabelNames.RootLabel + "/Tel Aviv/1042 — North";
    private const string SampleProjectLabelDisplay = "1 — A";

    internal sealed class LabelGroupingEmailGateway : IEmailGateway
    {
        internal static readonly IReadOnlyList<GmailLabelInfo> Labels =
        [
            new GmailLabelInfo("Label_Work", "Work"),
            new GmailLabelInfo("Label_Clients", "Clients"),
        ];

        public int MailboxPageCalls { get; private set; }

        public List<(EmailMailboxQuery Query, string? PageToken)> LabelPageCalls { get; } = [];

        public EmailMailboxQuery? LastLabelGroupQuery { get; private set; }

        public bool DuplicateSecondLabelPage { get; set; }

        public string? FailLabelPageOnToken { get; set; }

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(query.LabelId))
            {
                LabelPageCalls.Add((query, pageToken));
                LastLabelGroupQuery = query;

                if (!string.IsNullOrEmpty(FailLabelPageOnToken) && pageToken == FailLabelPageOnToken)
                {
                    throw new InvalidOperationException("Label page failed");
                }

                if (pageToken is null)
                {
                    return Task.FromResult(new EmailMailboxPage(
                    [
                        CreateSummary("label-work-page-1", "Work extra"),
                    ],
                    query.PageSize,
                    "label-Label_Work-page-2",
                    true));
                }

                if (pageToken == "label-Label_Work-page-2")
                {
                    var messageId = DuplicateSecondLabelPage ? "label-work-page-1" : "label-work-page-2";
                    return Task.FromResult(new EmailMailboxPage(
                    [
                        CreateSummary(messageId, "Work page 2"),
                    ],
                    query.PageSize,
                    null,
                    false));
                }

                return Task.FromResult(new EmailMailboxPage([], query.PageSize, null, false));
            }

            MailboxPageCalls++;
            return Task.FromResult(new EmailMailboxPage(
            [
                CreateSummary(
                    "msg-multi",
                    "Multi label",
                    ["INBOX", "Work", "Clients"],
                    "Work"),
                CreateSummary(
                    "msg-work-only",
                    "Work only",
                    ["INBOX", "Work"],
                    "Work"),
            ],
            query.PageSize,
            "global-page-2",
            true));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Labels);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxUnreadCount(0, IsExact: true));

        internal static EmailSummary CreateSummary(
            string messageId,
            string subject,
            IReadOnlyList<string>? labelNames = null,
            string? primaryLabel = null) =>
            new(
                messageId,
                $"thread-{messageId}",
                EmailAddress.CreateOrFallback($"{messageId}@example.com"),
                subject,
                DateTimeOffset.UtcNow,
                0,
                LabelNames: labelNames ?? ["Work"],
                PrimaryLabel: primaryLabel ?? "Work");
    }
    internal sealed class ProjectDedupeEmailGateway : IEmailGateway
    {
        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(query.OptionalProjectLabel))
            {
                var allItems = Enumerable.Range(1, 15)
                    .Select(i => new EmailSummary(
                        $"proj-{i}",
                        $"thread-{i}",
                        EmailAddress.CreateOrFallback($"user{i}@example.com"),
                        $"Subject {i}",
                        DateTimeOffset.UtcNow.AddHours(-i),
                        0))
                    .ToList();

                if (pageToken is null)
                {
                    var pageItems = allItems.Take(query.PageSize).ToList();
                    var hasNext = pageItems.Count < allItems.Count;
                    return Task.FromResult(new EmailMailboxPage(
                        pageItems,
                        query.PageSize,
                        hasNext ? "project-page-2" : null,
                        hasNext));
                }

                if (pageToken == "project-page-2")
                {
                    return Task.FromResult(new EmailMailboxPage(allItems.Skip(10).ToList(), query.PageSize, null, false));
                }
            }

            return Task.FromResult(new EmailMailboxPage(
            [
                CreateSummary("inbox-only", "Inbox only"),
                CreateSummary("proj-1", "Overlap with project"),
                CreateSummary("proj-5", "Another overlap"),
            ],
            query.PageSize,
            null,
            false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxUnreadCount(0, IsExact: true));

        internal static EmailSummary CreateSummary(string messageId, string subject) =>
            new(
                messageId,
                $"thread-{messageId}",
                EmailAddress.CreateOrFallback($"{messageId}@example.com"),
                subject,
                DateTimeOffset.UtcNow,
                0);
    }

    internal sealed class ProjectMergeEmailGateway : IEmailGateway
    {
        internal static readonly IReadOnlyList<GmailLabelInfo> Labels =
        [
            new GmailLabelInfo("Label_1A", SampleProjectLabelDisplay),
            new GmailLabelInfo("Label_Work", "Work"),
        ];

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(query.OptionalProjectLabel))
            {
                var allItems = Enumerable.Range(1, 10)
                    .Select(i => new EmailSummary(
                        $"proj-{i}",
                        $"thread-{i}",
                        EmailAddress.CreateOrFallback($"user{i}@example.com"),
                        $"Subject {i}",
                        DateTimeOffset.UtcNow.AddHours(-i),
                        0))
                    .ToList();

                return Task.FromResult(new EmailMailboxPage(allItems, query.PageSize, null, false));
            }

            return Task.FromResult(new EmailMailboxPage(
            [
                CreateSummary("mail-extra", "Extra in project label", ["INBOX", SampleProjectLabelDisplay], SampleProjectLabelDisplay),
                CreateSummary("mail-work", "Work mail", ["INBOX", "Work"], "Work"),
            ],
            query.PageSize,
            null,
            false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Labels);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxUnreadCount(0, IsExact: true));

        internal static EmailSummary CreateSummary(
            string messageId,
            string subject,
            IReadOnlyList<string> labelNames,
            string primaryLabel) =>
            new(
                messageId,
                $"thread-{messageId}",
                EmailAddress.CreateOrFallback($"{messageId}@example.com"),
                subject,
                DateTimeOffset.UtcNow,
                0,
                LabelNames: labelNames,
                PrimaryLabel: primaryLabel);
    }

    internal sealed class ProjectEmailGateway : IEmailGateway
    {
        public int MailboxPageCalls { get; private set; }

        public int ProjectPageCalls { get; private set; }

        public string? LastProjectLabel { get; private set; }

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default)
        {
            LastProjectLabel = projectLabelName;
            var items = Enumerable.Range(1, 15)
                .Select(i => new EmailSummary(
                    $"proj-{i}",
                    $"thread-{i}",
                    EmailAddress.CreateOrFallback($"user{i}@example.com"),
                    $"Subject {i}",
                    DateTimeOffset.UtcNow.AddHours(-i),
                    0))
                .ToList();

            return Task.FromResult<IReadOnlyList<EmailSummary>>(items);
        }

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(query.OptionalProjectLabel))
            {
                ProjectPageCalls++;
                LastProjectLabel = query.OptionalProjectLabel;
                var allItems = Enumerable.Range(1, 15)
                    .Select(i => new EmailSummary(
                        $"proj-{i}",
                        $"thread-{i}",
                        EmailAddress.CreateOrFallback($"user{i}@example.com"),
                        $"Subject {i}",
                        DateTimeOffset.UtcNow.AddHours(-i),
                        0))
                    .ToList();

                if (pageToken is null)
                {
                    var pageItems = allItems.Take(query.PageSize).ToList();
                    var hasNext = pageItems.Count < allItems.Count;
                    return Task.FromResult(new EmailMailboxPage(
                        pageItems,
                        query.PageSize,
                        hasNext ? "project-page-2" : null,
                        hasNext));
                }

                if (pageToken == "project-page-2")
                {
                    var pageItems = allItems.Skip(10).ToList();
                    return Task.FromResult(new EmailMailboxPage(pageItems, query.PageSize, null, false));
                }
            }

            MailboxPageCalls++;
            return Task.FromResult(new EmailMailboxPage([], query.PageSize, null, false));
        }

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxUnreadCount(0, IsExact: true));
    }

    internal class PagingEmailGateway : IEmailGateway
    {
        public bool FailOnSecondPage { get; set; }

        public int MailboxPageCalls { get; protected set; }

        public int UnreadCountCalls { get; private set; }

        public int ConfiguredUnreadTotal { get; set; } = 3;

        public EmailMailboxQuery? LastQuery { get; private set; }

        public EmailMailboxQuery? LastUnreadQuery { get; private set; }

        public string? LastPageToken { get; private set; }

        public string? LastNextToken { get; private set; }

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public virtual Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public virtual Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public virtual Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            MailboxPageCalls++;
            if (FailOnSecondPage && pageToken is not null)
            {
                throw new InvalidOperationException("Gmail page failed");
            }

            LastQuery = query;
            LastPageToken = pageToken;
            return Task.FromResult(new EmailMailboxPage(
            [
                new EmailSummary(
                    "msg-1",
                    "thread-1",
                    EmailAddress.CreateOrFallback("a@example.com"),
                    "Hello",
                    DateTimeOffset.UtcNow,
                    0,
                    InternetMessageId: "<abc@mail.com>"),
            ],
            query.PageSize,
            LastNextToken = "page-2",
            true));
        }

        public virtual Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default)
        {
            UnreadCountCalls++;
            LastUnreadQuery = query;
            return Task.FromResult(new EmailMailboxUnreadCount(ConfiguredUnreadTotal, IsExact: true));
        }
    }

    internal class UnreadPagingEmailGateway : IEmailGateway
    {
        public bool ReturnUnreadRows { get; set; } = true;

        public int UnreadCountCalls { get; private set; }

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxPage(
            [
                new EmailSummary(
                    "msg-unread",
                    "thread-1",
                    EmailAddress.CreateOrFallback("a@example.com"),
                    "Unread",
                    DateTimeOffset.UtcNow,
                    0,
                    IsUnread: ReturnUnreadRows),
                new EmailSummary(
                    "msg-read",
                    "thread-2",
                    EmailAddress.CreateOrFallback("b@example.com"),
                    "Read",
                    DateTimeOffset.UtcNow,
                    0,
                    IsUnread: false),
            ],
            query.PageSize,
            null,
            false));

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default)
        {
            UnreadCountCalls++;
            return Task.FromResult(new EmailMailboxUnreadCount(5, IsExact: true));
        }
    }
    internal class ActionTestEmailGateway : PagingEmailGateway
    {
        private readonly Dictionary<string, EmailSummary> _summariesById = new(StringComparer.Ordinal);

        public ActionTestEmailGateway()
        {
            ConfigureFiledSummary("msg-1");
        }

        public void ConfigureFiledSummary(string messageId)
        {
            _summariesById[messageId] = new EmailSummary(
                messageId,
                "thread-1",
                EmailAddress.CreateOrFallback("a@example.com"),
                "Hello",
                DateTimeOffset.UtcNow,
                0,
                InternetMessageId: "<abc@mail.com>",
                LabelNames: ["INBOX", SampleFiledProjectLabelPath]);
        }

        public void ConfigureUnfiledSummary(string messageId)
        {
            _summariesById[messageId] = new EmailSummary(
                messageId,
                "thread-1",
                EmailAddress.CreateOrFallback("a@example.com"),
                "Hello",
                DateTimeOffset.UtcNow,
                0,
                InternetMessageId: "<abc@mail.com>",
                LabelNames: ["INBOX"]);
        }

        public void ConfigurePendingSummary(string messageId)
        {
            _summariesById[messageId] = new EmailSummary(
                messageId,
                "thread-1",
                EmailAddress.CreateOrFallback("a@example.com"),
                "Hello",
                DateTimeOffset.UtcNow,
                0,
                InternetMessageId: "<abc@mail.com>",
                LabelNames: ["INBOX", EmailGmailLabelNames.Pending],
                PrimaryLabel: EmailGmailLabelNames.Pending);
        }

        public override Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_summariesById.TryGetValue(messageId, out var summary) ? summary : null);
    }

    internal sealed class TwoRowActionTestEmailGateway : ActionTestEmailGateway
    {
        public override Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            MailboxPageCalls++;
            return Task.FromResult(new EmailMailboxPage(
            [
                new EmailSummary(
                    "msg-1",
                    "thread-1",
                    EmailAddress.CreateOrFallback("a@example.com"),
                    "Hello",
                    DateTimeOffset.UtcNow,
                    0,
                    InternetMessageId: "<abc@mail.com>"),
                new EmailSummary(
                    "msg-2",
                    "thread-1",
                    EmailAddress.CreateOrFallback("b@example.com"),
                    "Follow up",
                    DateTimeOffset.UtcNow,
                    0,
                    InternetMessageId: "<def@mail.com>"),
            ],
            query.PageSize,
            null,
            false));
        }
    }

    internal sealed class RegroupingActionEmailGateway : ActionTestEmailGateway
    {
        public RegroupingActionEmailGateway(bool loadFiledInitially = true)
        {
            LoadFiledInitially = loadFiledInitially;
            if (loadFiledInitially)
            {
                ConfigureFiledSummary("msg-1");
            }
            else
            {
                ConfigureUnfiledSummary("msg-1");
            }
        }

        public bool LoadFiledInitially { get; }

        public override Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<GmailLabelInfo> labels =
            [
                new GmailLabelInfo("INBOX", "INBOX"),
                new GmailLabelInfo("Label_Project", SampleFiledProjectLabelPath),
                new GmailLabelInfo("Label_Pending", EmailGmailLabelNames.Pending),
            ];
            return Task.FromResult(labels);
        }

        public override Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            MailboxPageCalls++;
            var labelNames = LoadFiledInitially
                ? new[] { "INBOX", SampleFiledProjectLabelPath }
                : new[] { "INBOX" };
            var primaryLabel = LoadFiledInitially
                ? SampleFiledProjectLabelPath
                : "INBOX";

            return Task.FromResult(new EmailMailboxPage(
            [
                new EmailSummary(
                    "msg-1",
                    "thread-1",
                    EmailAddress.CreateOrFallback("a@example.com"),
                    "Hello",
                    DateTimeOffset.UtcNow,
                    0,
                    InternetMessageId: "<abc@mail.com>",
                    LabelNames: labelNames,
                    PrimaryLabel: primaryLabel),
            ],
            query.PageSize,
            null,
            false));
        }
    }

    internal sealed class AttachmentCountEmailGateway : IEmailGateway
    {
        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsAsync(
            string location,
            string projectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<IReadOnlyList<EmailSummary>> GetProjectEmailsByProjectLabelAsync(
            string projectLabelName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmailSummary>>([]);

        public Task<EmailSummary?> GetByIdAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailSummary?>(null);

        public Task<EmailMessageDetails?> GetDetailsAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmailMessageDetails?>(null);

        public Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxPage(
            [
                new EmailSummary(
                    "msg-att",
                    "thread-att",
                    EmailAddress.CreateOrFallback("a@example.com"),
                    "Attachments",
                    DateTimeOffset.UtcNow,
                    3),
            ],
            query.PageSize,
            null,
            false));

        public Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>([]);

        public Task<EmailMailboxUnreadCount> GetMailboxUnreadCountAsync(
            EmailMailboxQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailMailboxUnreadCount(0, IsExact: true));
    }
}

