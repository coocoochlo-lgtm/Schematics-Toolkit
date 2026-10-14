namespace REDox.Benchmarks.Data;

[DataSource("external/simdjson-data/jsonexamples/twitter.json")]
public sealed class Twitter
{
    public sealed class Root
    {
        public Status[]? statuses { get; set; }

        public Search_Metadata? search_metadata { get; set; }
    }

    public class Search_Metadata
    {
        public float completed_in { get; set; }
        public long max_id { get; set; }
        public string? max_id_str { get; set; }
        public string? next_results { get; set; }
        public string? query { get; set; }
        public string? refresh_url { get; set; }
        public int count { get; set; }
        public int since_id { get; set; }
        public string? since_id_str { get; set; }
    }

    public class Status
    {
        public Metadata? metadata { get; set; }
        public string? created_at { get; set; }
        public long id { get; set; }
        public string? id_str { get; set; }
        public string? text { get; set; }
        public string? source { get; set; }
        public bool truncated { get; set; }
        public long? in_reply_to_status_id { get; set; }
        public string? in_reply_to_status_id_str { get; set; }
        public long? in_reply_to_user_id { get; set; }
        public string? in_reply_to_user_id_str { get; set; }
        public string? in_reply_to_screen_name { get; set; }
        public User? user { get; set; }
        public object? geo { get; set; }
        public object? coordinates { get; set; }
        public object? place { get; set; }
        public object? contributors { get; set; }
        public int retweet_count { get; set; }
        public int favorite_count { get; set; }
        public Entities1? entities { get; set; }
        public bool favorited { get; set; }
        public bool retweeted { get; set; }
        public string? lang { get; set; }
        public Retweeted_Status? retweeted_status { get; set; }
        public bool possibly_sensitive { get; set; }
    }

    public class Metadata
    {
        public string? result_type { get; set; }
        public string? iso_language_code { get; set; }
    }

    public class User
    {
        public long id { get; set; }
        public string? id_str { get; set; }
        public string? name { get; set; }
        public string? screen_name { get; set; }
        public string? location { get; set; }
        public string? description { get; set; }
        public string? url { get; set; }
        public Entities? entities { get; set; }
        public bool @protected { get; set; }
        public int followers_count { get; set; }
        public int friends_count { get; set; }
        public int listed_count { get; set; }
        public string? created_at { get; set; }
        public int favourites_count { get; set; }
        public int? utc_offset { get; set; }
        public string? time_zone { get; set; }
        public bool geo_enabled { get; set; }
        public bool verified { get; set; }
        public int statuses_count { get; set; }
        public string? lang { get; set; }
        public bool contributors_enabled { get; set; }
        public bool is_translator { get; set; }
        public bool is_translation_enabled { get; set; }
        public string? profile_background_color { get; set; }
        public string? profile_background_image_url { get; set; }
        public string? profile_background_image_url_https { get; set; }
        public bool profile_background_tile { get; set; }
        public string? profile_image_url { get; set; }
        public string? profile_image_url_https { get; set; }
        public string? profile_banner_url { get; set; }
        public string? profile_link_color { get; set; }
        public string? profile_sidebar_border_color { get; set; }
        public string? profile_sidebar_fill_color { get; set; }
        public string? profile_text_color { get; set; }
        public bool profile_use_background_image { get; set; }
        public bool default_profile { get; set; }
        public bool default_profile_image { get; set; }
        public bool following { get; set; }
        public bool follow_request_sent { get; set; }
        public bool notifications { get; set; }
    }

    public class Entities
    {
        public Description? description { get; set; }
        public Url1? url { get; set; }
    }

    public class Description
    {
        public Url[]? urls { get; set; }
    }

    public class Url
    {
        public string? url { get; set; }
        public string? expanded_url { get; set; }
        public string? display_url { get; set; }
        public int[]? indices { get; set; }
    }

    public class Url1
    {
        public Url2[]? urls { get; set; }
    }

    public class Url2
    {
        public string? url { get; set; }
        public string? expanded_url { get; set; }
        public string? display_url { get; set; }
        public int[]? indices { get; set; }
    }

    public class Entities1
    {
        public Hashtag[]? hashtags { get; set; }
        public object[]? symbols { get; set; }
        public Url3[]? urls { get; set; }
        public User_Mentions[]? user_mentions { get; set; }
        public Medium[]? media { get; set; }
    }

    public class Hashtag
    {
        public string? text { get; set; }
        public int[]? indices { get; set; }
    }

    public class Url3
    {
        public string? url { get; set; }
        public string? expanded_url { get; set; }
        public string? display_url { get; set; }
        public int[]? indices { get; set; }
    }

    public class User_Mentions
    {
        public string? screen_name { get; set; }
        public string? name { get; set; }
        public long id { get; set; }
        public string? id_str { get; set; }
        public int[]? indices { get; set; }
    }

    public class Medium
    {
        public long id { get; set; }
        public string? id_str { get; set; }
        public int[]? indices { get; set; }
        public string? media_url { get; set; }
        public string? media_url_https { get; set; }
        public string? url { get; set; }
        public string? display_url { get; set; }
        public string? expanded_url { get; set; }
        public string? type { get; set; }
        public Sizes? sizes { get; set; }
        public long source_status_id { get; set; }
        public string? source_status_id_str { get; set; }
    }

    public class Sizes
    {
        public Medium1? medium { get; set; }
        public Small? small { get; set; }
        public Thumb? thumb { get; set; }
        public Large? large { get; set; }
    }

    public class Medium1
    {
        public int w { get; set; }
        public int h { get; set; }
        public string? resize { get; set; }
    }

    public class Small
    {
        public int w { get; set; }
        public int h { get; set; }
        public string? resize { get; set; }
    }

    public class Thumb
    {
        public int w { get; set; }
        public int h { get; set; }
        public string? resize { get; set; }
    }

    public class Large
    {
        public int w { get; set; }
        public int h { get; set; }
        public string? resize { get; set; }
    }

    public class Retweeted_Status
    {
        public Metadata1? metadata { get; set; }
        public string? created_at { get; set; }
        public long id { get; set; }
        public string? id_str { get; set; }
        public string? text { get; set; }
        public string? source { get; set; }
        public bool truncated { get; set; }
        public long? in_reply_to_status_id { get; set; }
        public string? in_reply_to_status_id_str { get; set; }
        public long? in_reply_to_user_id { get; set; }
        public string? in_reply_to_user_id_str { get; set; }
        public string? in_reply_to_screen_name { get; set; }
        public User1? user { get; set; }
        public object? geo { get; set; }
        public object? coordinates { get; set; }
        public object? place { get; set; }
        public object? contributors { get; set; }
        public int retweet_count { get; set; }
        public int favorite_count { get; set; }
        public Entities3? entities { get; set; }
        public bool favorited { get; set; }
        public bool retweeted { get; set; }
        public bool possibly_sensitive { get; set; }
        public string? lang { get; set; }
    }

    public class Metadata1
    {
        public string? result_type { get; set; }
        public string? iso_language_code { get; set; }
    }

    public class User1
    {
        public long id { get; set; }
        public string? id_str { get; set; }
        public string? name { get; set; }
        public string? screen_name { get; set; }
        public string? location { get; set; }
        public string? description { get; set; }
        public string? url { get; set; }
        public Entities2? entities { get; set; }
        public bool @protected { get; set; }
        public int followers_count { get; set; }
        public int friends_count { get; set; }
        public int listed_count { get; set; }
        public string? created_at { get; set; }
        public int favourites_count { get; set; }
        public int? utc_offset { get; set; }
        public string? time_zone { get; set; }
        public bool geo_enabled { get; set; }
        public bool verified { get; set; }
        public int statuses_count { get; set; }
        public string? lang { get; set; }
        public bool contributors_enabled { get; set; }
        public bool is_translator { get; set; }
        public bool is_translation_enabled { get; set; }
        public string? profile_background_color { get; set; }
        public string? profile_background_image_url { get; set; }
        public string? profile_background_image_url_https { get; set; }
        public bool profile_background_tile { get; set; }
        public string? profile_image_url { get; set; }
        public string? profile_image_url_https { get; set; }
        public string? profile_banner_url { get; set; }
        public string? profile_link_color { get; set; }
        public string? profile_sidebar_border_color { get; set; }
        public string? profile_sidebar_fill_color { get; set; }
        public string? profile_text_color { get; set; }
        public bool profile_use_background_image { get; set; }
        public bool default_profile { get; set; }
        public bool default_profile_image { get; set; }
        public bool following { get; set; }
        public bool follow_request_sent { get; set; }
        public bool notifications { get; set; }
    }

    public class Entities2
    {
        public Description1? description { get; set; }
        public Url5? url { get; set; }
    }

    public class Description1
    {
        public Url4[]? urls { get; set; }
    }

    public class Url4
    {
        public string? url { get; set; }
        public string? expanded_url { get; set; }
        public string? display_url { get; set; }
        public int[]? indices { get; set; }
    }

    public class Url5
    {
        public Url6[]? urls { get; set; }
    }

    public class Url6
    {
        public string? url { get; set; }
        public string? expanded_url { get; set; }
        public string? display_url { get; set; }
        public int[]? indices { get; set; }
    }

    public class Entities3
    {
        public Hashtag1[]? hashtags { get; set; }
        public object[]? symbols { get; set; }
        public Url7[]? urls { get; set; }
        public User_Mentions1[]? user_mentions { get; set; }
        public Medium2[]? media { get; set; }
    }

    public class Hashtag1
    {
        public string? text { get; set; }
        public int[]? indices { get; set; }
    }

    public class Url7
    {
        public string? url { get; set; }
        public string? expanded_url { get; set; }
        public string? display_url { get; set; }
        public int[]? indices { get; set; }
    }

    public class User_Mentions1
    {
        public string? screen_name { get; set; }
        public string? name { get; set; }
        public long id { get; set; }
        public string? id_str { get; set; }
        public int[]? indices { get; set; }
    }

    public class Medium2
    {
        public long id { get; set; }
        public string? id_str { get; set; }
        public int[]? indices { get; set; }
        public string? media_url { get; set; }
        public string? media_url_https { get; set; }
        public string? url { get; set; }
        public string? display_url { get; set; }
        public string? expanded_url { get; set; }
        public string? type { get; set; }
        public Sizes1? sizes { get; set; }
        public long source_status_id { get; set; }
        public string? source_status_id_str { get; set; }
    }

    public class Sizes1
    {
        public Medium3? medium { get; set; }
        public Small1? small { get; set; }
        public Thumb1? thumb { get; set; }
        public Large1? large { get; set; }
    }

    public class Medium3
    {
        public int w { get; set; }
        public int h { get; set; }
        public string? resize { get; set; }
    }

    public class Small1
    {
        public int w { get; set; }
        public int h { get; set; }
        public string? resize { get; set; }
    }

    public class Thumb1
    {
        public int w { get; set; }
        public int h { get; set; }
        public string? resize { get; set; }
    }

    public class Large1
    {
        public int w { get; set; }
        public int h { get; set; }
        public string? resize { get; set; }
    }
}