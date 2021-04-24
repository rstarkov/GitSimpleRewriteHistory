using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibGit2Sharp;
using Microsoft.WindowsAPICodePack.Dialogs;
using PropertyChanged;
using RT.Util.Forms;

// window size/position settings
// export/import xml
// "Undo all", "all committer equal"

namespace GitSimpleRewriteHistory
{
    public partial class MainWindow : Window
    {
        private MainModel _model = new MainModel();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _model;
            _model.InitialLoadRepo(".");
        }

        private void UndoChanges_Click(object sender, RoutedEventArgs e)
        {
            ((CommitModel) ((Control) sender).DataContext).UndoChanges();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CommonOpenFileDialog();
            dlg.IsFolderPicker = true;
            dlg.EnsurePathExists = true;
            dlg.Title = "Select a folder containing a Git repository";
            dlg.InitialDirectory = _model.RepoPath;
            var result = dlg.ShowDialog();
            if (result != CommonFileDialogResult.Ok)
                return;
            _model.LoadRepo(dlg.FileName);
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            _model.ReloadRepo();
            DlgMessage.ShowInfo("Repository has been reloaded.");
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!_model.AnyChanges)
            {
                DlgMessage.ShowInfo("There are no pending changes to apply.");
                return;
            }
            var btn = DlgMessage.ShowQuestion("Apply pending changes to this repository?\nRemember that changing history has consequences that need to be understood.\n\nWARNING: MAKE A BACKUP OF YOUR REPOSITORY.\nThis program modifies the repository in-place using LibGit2Sharp. This program has not been through extensive testing, and could well damage or destroy your repository. Seriously, do not skip the backup.",
                "&Continue at my own risk", "More &about rewriting history", "Cancel");
            if (btn == 2)
                return;
            if (btn == 1)
            {
                Process.Start("http://stackoverflow.com/a/1491022/33080");
                return;
            }
            _model.ApplyChanges();
        }
    }

    [AddINotifyPropertyChangedInterface]
    class MainModel
    {
        public string RepoPath { get; set; } = "";
        public List<CommitModel> Commits { get; set; } = new List<CommitModel>();
        public bool AnyChanges { get { return Commits.Any(r => r.Modified); } }

        public void InitialLoadRepo(string path)
        {
            RepoPath = Path.GetFullPath(path);
            Repository repo;
            try
            {
                repo = new Repository(path);
            }
            catch
            {
                Commits = new List<CommitModel>();
                return;
            }
            Commits = repo.Commits.Select(commit => new CommitModel(commit)).ToList();
        }

        public void LoadRepo(string path)
        {
            Repository repo;
            try
            {
                repo = new Repository(path);
            }
            catch
            {
                DlgMessage.ShowWarning("There is no valid Git repository at this path.");
                return;
            }
            if (LoadCommitsPreserveChanges(repo.Commits.Select(commit => new CommitModel(commit)).ToList(), true))
                RepoPath = Path.GetFullPath(path);
        }

        public void ReloadRepo()
        {
            Repository repo;
            try
            {
                repo = new Repository(RepoPath);
            }
            catch
            {
                DlgMessage.ShowWarning("It looks like there is no valid Git repository at this path anymore. Please use “Browse” to load from another path.");
                return;
            }
            LoadCommitsPreserveChanges(repo.Commits.Select(commit => new CommitModel(commit)).ToList(), true);
        }

        private bool LoadCommitsPreserveChanges(List<CommitModel> newCommits, bool warnIfNoMatch)
        {
            var oldCommits = Commits;
            bool warned = false;
            foreach (var oldCommit in oldCommits.Where(or => or.Modified))
            {
                var newCommit = newCommits.SingleOrDefault(r => r.Hash == oldCommit.Hash);
                if (newCommit != null)
                    newCommit.RestoreFrom(oldCommit);
                else if (warnIfNoMatch && !warned)
                {
                    var btn = DlgMessage.ShowWarning($"You have made changes to commit {oldCommit.Hash} but the repository you are opening does not have this commit.\nIf you choose to proceed, your changes to this commit will be discarded.\n\nDo you wish to proceed, discarding your change?",
                        "&Yes", "Yes to &all", "Cancel");
                    if (btn == 2)
                        return false;
                    if (btn == 1)
                        warned = true;
                }
            }
            Commits = newCommits;
            return true;
        }

        public void ApplyChanges()
        {
            Repository repo;
            try
            {
                repo = new Repository(RepoPath);
            }
            catch
            {
                DlgMessage.ShowWarning("It looks like there is no valid Git repository at this path anymore. Please use “Browse” to load from another path.");
                return;
            }

            var knownCommits = Commits.ToDictionary(c => c.Hash);
            var loadedCommits = new HashSet<string>(repo.Commits.Select(c => c.Id.Sha));
            if (knownCommits.Count != loadedCommits.Count || knownCommits.Keys.Any(c => !loadedCommits.Contains(c)) || loadedCommits.Any(c => !knownCommits.ContainsKey(c))
                || repo.Commits.Any(cl => !knownCommits[cl.Id.Sha].MatchesOriginal(cl)))
            {
                DlgMessage.ShowWarning("The repository at this path has changed since it was last loaded. Please use Reload before applying changes.");
                return;
            }

            foreach (var rf in repo.Refs.Where(rf => rf.CanonicalName.StartsWith("refs/original/")).ToList())
                repo.Refs.Remove(rf);

            repo.Refs.RewriteHistory(new RewriteHistoryOptions
            {
                CommitHeaderRewriter = arg =>
                {
                    var ours = knownCommits[arg.Id.Sha];
                    return new CommitRewriteInfo
                    {
                        Author = new Signature(ours.AuthorName, ours.AuthorEmail, ours.AuthorDate),
                        Committer = new Signature(ours.CommitterName, ours.CommitterEmail, ours.CommitterDate),
                        Message = ours.Message
                    };
                }
            }, repo.Commits);
            InitialLoadRepo(RepoPath);
            DlgMessage.ShowInfo("Changes applied successfully.");
        }
    }

    [AddINotifyPropertyChangedInterface]
    class CommitModel
    {
        private Commit _commit;

        public CommitModel(Commit commit)
        {
            _commit = commit;
            UndoChanges();
        }

        public void UndoChanges()
        {
            AuthorName = _commit.Author.Name;
            AuthorEmail = _commit.Author.Email;
            AuthorDate = _commit.Author.When;
            CommitterName = _commit.Committer.Name;
            CommitterEmail = _commit.Committer.Email;
            CommitterDate = _commit.Committer.When;

            CommitterEqualsAuthor = (AuthorName == CommitterName) && (AuthorEmail == CommitterEmail) && (AuthorDate == CommitterDate);

            Hash = _commit.Id.Sha;
            Message = _commit.Message;
        }

        public void RestoreFrom(CommitModel other)
        {
            AuthorName = other.AuthorName;
            AuthorEmail = other.AuthorEmail;
            AuthorDate = other.AuthorDate;
            CommitterName = other.CommitterName;
            CommitterEmail = other.CommitterEmail;
            CommitterDate = other.CommitterDate;

            CommitterEqualsAuthor = (AuthorName == CommitterName) && (AuthorEmail == CommitterEmail) && (AuthorDate == CommitterDate);

            Message = other.Message;
        }

        public bool MatchesOriginal(Commit c)
        {
            return c.Id.Sha == _commit.Id.Sha && c.Message == _commit.Message
                && c.Author.Name == _commit.Author.Name && c.Author.Email == _commit.Author.Email && c.Author.When == _commit.Author.When
                && c.Committer.Name == _commit.Committer.Name && c.Committer.Email == _commit.Committer.Email && c.Committer.When == _commit.Committer.When;
        }

        public string Hash { get; set; }
        public string Message { get; set; }

        public string AuthorName
        {
            get { return _authorName; }
            set
            {
                _authorName = value;
                if (CommitterEqualsAuthor)
                    CommitterName = value;
            }
        }
        private string _authorName;

        public string AuthorEmail
        {
            get { return _authorEmail; }
            set
            {
                _authorEmail = value;
                if (CommitterEqualsAuthor)
                    CommitterEmail = value;
            }
        }
        private string _authorEmail;

        public DateTimeOffset AuthorDate
        {
            get { return _authorDate; }
            set
            {
                _authorDate = value;
                if (CommitterEqualsAuthor)
                    CommitterDate = value;
            }
        }
        private DateTimeOffset _authorDate;

        public string CommitterName { get; set; }
        public string CommitterEmail { get; set; }
        public DateTimeOffset CommitterDate { get; set; }

        public bool CommitterEqualsAuthor
        {
            get { return _committerEqualsAuthor; }
            set
            {
                _committerEqualsAuthor = value;
                if (_committerEqualsAuthor)
                {
                    CommitterName = AuthorName;
                    CommitterEmail = AuthorEmail;
                    CommitterDate = AuthorDate;
                }
            }
        }
        public bool CommitterNotEqualsAuthor { get { return !CommitterEqualsAuthor; } set { CommitterEqualsAuthor = !value; } }
        private bool _committerEqualsAuthor;

        public bool Modified
        {
            get
            {
                return !(Message == _commit.Message
                    && AuthorName == _commit.Author.Name
                    && AuthorEmail == _commit.Author.Email
                    && AuthorDate == _commit.Author.When
                    && CommitterName == _commit.Committer.Name
                    && CommitterEmail == _commit.Committer.Email
                    && CommitterDate == _commit.Committer.When);
            }
        }

        public Visibility UndoVisibility { get { return Modified ? Visibility.Visible : Visibility.Collapsed; } }
        public Brush ListItemBackground { get { return Modified ? new SolidColorBrush(Colors.Tan) : null; } }
    }
}
