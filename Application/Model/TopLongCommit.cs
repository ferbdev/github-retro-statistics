namespace Application.Model
{
    public class TopLongCommit
    {
        public string RepoName { get; set; }
        public int TotalChangedLines { get; set; }
        public string Date { get; set; }
    }
}
