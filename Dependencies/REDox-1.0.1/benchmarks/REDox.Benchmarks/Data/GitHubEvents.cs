namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/github_events.json")]
public class GitHubEvents
{
    public class GitHubEvent
    {
        public string? type { get; set; }
        public string? created_at { get; set; } // DateTime型でも可ですが、形式変換を避けるためstringにしています
        public Actor? actor { get; set; }
        public RepoShort? repo { get; set; }
        public bool @public { get; set; } // "public" は予約語なので @ を付与
        public Payload? payload { get; set; }
        public string? id { get; set; }
        public Actor? org { get; set; } // orgがない場合もあるのでnull許容
    }

    public class Actor
    {
        public string? gravatar_id { get; set; }
        public string? login { get; set; }
        public string? avatar_url { get; set; }
        public string? url { get; set; }
        public long id { get; set; }
    }

    public class RepoShort
    {
        public string? url { get; set; }
        public long id { get; set; }
        public string? name { get; set; }
    }

    // イベントタイプによって中身が異なるため、
    // ファイルに出現するすべてのプロパティを網羅したクラスにしています
    public class Payload
    {
        // PushEvent用
        public Commit[]? commits { get; set; }
        public int distinct_size { get; set; }
        public long push_id { get; set; }
        public string? head { get; set; }
        public string? before { get; set; }
        public int size { get; set; }

        // CreateEvent, PushEvent, etc用
        public string? @ref { get; set; }
        public string? description { get; set; }
        public string? master_branch { get; set; }
        public string? ref_type { get; set; }

        // ForkEvent用
        public Repository? forkee { get; set; }

        // WatchEvent, IssuesEvent, IssueCommentEvent, GollumEvent用
        public string? action { get; set; }

        // IssueCommentEvent, IssuesEvent用
        public Issue? issue { get; set; }
        public Comment? comment { get; set; }

        // GollumEvent用
        public Page[]? pages { get; set; }
    }

    public class Commit
    {
        public string? url { get; set; }
        public string? message { get; set; }
        public bool distinct { get; set; }
        public string? sha { get; set; }
        public Author2? author { get; set; }
    }

    public class Author2
    {
        public string? email { get; set; }
        public string? name { get; set; }
    }

    public class Repository
    {
        public string? description { get; set; }
        public bool fork { get; set; }
        public string? url { get; set; }
        public string? language { get; set; }
        public string? stargazers_url { get; set; }
        public string? clone_url { get; set; }
        public string? tags_url { get; set; }
        public string? full_name { get; set; }
        public string? merges_url { get; set; }
        public int forks { get; set; }
        public bool @private { get; set; }
        public string? git_refs_url { get; set; }
        public string? archive_url { get; set; }
        public string? collaborators_url { get; set; }
        public User3? owner { get; set; }
        public string? languages_url { get; set; }
        public string? trees_url { get; set; }
        public string? labels_url { get; set; }
        public string? html_url { get; set; }
        public string? pushed_at { get; set; }
        public string? created_at { get; set; }
        public bool has_issues { get; set; }
        public string? forks_url { get; set; }
        public string? branches_url { get; set; }
        public string? commits_url { get; set; }
        public string? notifications_url { get; set; }
        public int open_issues { get; set; }
        public string? contents_url { get; set; }
        public string? blobs_url { get; set; }
        public string? issues_url { get; set; }
        public string? compare_url { get; set; }
        public string? issue_events_url { get; set; }
        public string? name { get; set; }
        public string? updated_at { get; set; }
        public string? statuses_url { get; set; }
        public int forks_count { get; set; }
        public string? assignees_url { get; set; }
        public string? ssh_url { get; set; }
        public bool @public { get; set; }
        public bool has_wiki { get; set; }
        public string? subscribers_url { get; set; }
        public string? mirror_url { get; set; }
        public int watchers_count { get; set; }
        public long id { get; set; }
        public bool has_downloads { get; set; }
        public string? git_commits_url { get; set; }
        public string? downloads_url { get; set; }
        public string? pulls_url { get; set; }
        public string? homepage { get; set; }
        public string? issue_comment_url { get; set; }
        public string? hooks_url { get; set; }
        public string? subscription_url { get; set; }
        public string? milestones_url { get; set; }
        public string? svn_url { get; set; }
        public string? events_url { get; set; }
        public string? git_tags_url { get; set; }
        public string? teams_url { get; set; }
        public string? comments_url { get; set; }
        public int open_issues_count { get; set; }
        public string? keys_url { get; set; }
        public string? git_url { get; set; }
        public string? contributors_url { get; set; }
        public int size { get; set; }
        public int watchers { get; set; }
    }

    public class User3
    {
        public string? url { get; set; }
        public string? gists_url { get; set; }
        public string? gravatar_id { get; set; }
        public string? type { get; set; }
        public string? avatar_url { get; set; }
        public string? subscriptions_url { get; set; }
        public string? organizations_url { get; set; }
        public string? received_events_url { get; set; }
        public string? repos_url { get; set; }
        public string? login { get; set; }
        public long id { get; set; }
        public string? starred_url { get; set; }
        public string? events_url { get; set; }
        public string? followers_url { get; set; }
        public string? following_url { get; set; }
    }

    public class Issue
    {
        public User3? user { get; set; }
        public string? url { get; set; }
        public object[]? labels { get; set; } // 内容が空のためobjectとしています
        public string? html_url { get; set; }
        public string? labels_url { get; set; }
        public PullRequest? pull_request { get; set; }
        public string? created_at { get; set; }
        public string? closed_at { get; set; }
        public object? milestone { get; set; }
        public string? title { get; set; }
        public string? body { get; set; }
        public string? updated_at { get; set; }
        public int number { get; set; }
        public string? state { get; set; }
        public User3? assignee { get; set; }
        public long id { get; set; }
        public string? events_url { get; set; }
        public string? comments_url { get; set; }
        public int comments { get; set; }
    }

    public class PullRequest
    {
        public string? html_url { get; set; }
        public string? patch_url { get; set; }
        public string? diff_url { get; set; }
    }

    public class Comment
    {
        public User3? user { get; set; }
        public string? url { get; set; }
        public string? issue_url { get; set; }
        public string? created_at { get; set; }
        public string? body { get; set; }
        public string? updated_at { get; set; }
        public long id { get; set; }
    }

    public class Page
    {
        public string? page_name { get; set; }
        public string? html_url { get; set; }
        public string? title { get; set; }
        public string? sha { get; set; }
        public string? summary { get; set; }
        public string? action { get; set; }
    }
}