﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using LibGit2Sharp;
using NerdBank.GitVersioning.Managed;
using Xunit;
using Xunit.Abstractions;

namespace NerdBank.GitVersioning.Tests.Managed
{
    public class GitRepositoryTests : RepoTestBase
    {
        public GitRepositoryTests(ITestOutputHelper logger)
            : base(logger)
        {
        }

        [Fact]
        public void CreateTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(1);

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                AssertPath(Path.Combine(this.RepoPath, ".git"), repository.CommonDirectory);
                AssertPath(Path.Combine(this.RepoPath, ".git"), repository.GitDirectory);
                AssertPath(this.RepoPath, repository.WorkingDirectory);
                AssertPath(Path.Combine(this.RepoPath, ".git", "objects"), repository.ObjectDirectory);
            }
        }

        [Fact]
        public void CreateWorkTreeTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(2);

            string workTreePath = this.CreateDirectoryForNewRepo();
            Directory.Delete(workTreePath);
            this.Repo.Worktrees.Add("HEAD~1", "myworktree", workTreePath, isLocked: false);

            using (var repository = GitRepository.Create(workTreePath))
            {
                AssertPath(Path.Combine(this.RepoPath, ".git"), repository.CommonDirectory);
                AssertPath(Path.Combine(this.RepoPath, ".git", "worktrees", "myworktree"), repository.GitDirectory);
                AssertPath(workTreePath, repository.WorkingDirectory);
                AssertPath(Path.Combine(this.RepoPath, ".git", "objects"), repository.ObjectDirectory);
            }
        }

        [Fact]
        public void CreateNotARepoTest()
        {
            Assert.Null(GitRepository.Create(null));
            Assert.Null(GitRepository.Create(""));
            Assert.Null(GitRepository.Create("/A/Path/To/A/Directory/Which/Does/Not/Exist"));
            Assert.Null(GitRepository.Create(this.RepoPath));
        }

        // A "normal" repository, where a branch is currently checked out.
        [Fact]
        public void GetHeadAsReferenceTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(2);

            var headObjectId = GitObjectId.Parse(this.Repo.Head.Tip.Sha);

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                var head = repository.GetHeadAsReferenceOrSha();
                var reference = Assert.IsType<string>(head);
                Assert.Equal("refs/heads/master", reference);

                Assert.Equal(headObjectId, repository.GetHeadCommitSha());

                var headCommit = repository.GetHeadCommit();
                Assert.NotNull(headCommit);
                Assert.Equal(headObjectId, headCommit.Value.Sha);
            }
        }

        // A repository with a detached HEAD.
        [Fact]
        public void GetHeadAsShaTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(2);

            var newHead = this.Repo.Head.Tip.Parents.Single();
            var newHeadObjectId = GitObjectId.Parse(newHead.Sha);
            Commands.Checkout(this.Repo, this.Repo.Head.Tip.Parents.Single());

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                var detachedHead = repository.GetHeadAsReferenceOrSha();
                var reference = Assert.IsType<GitObjectId>(detachedHead);
                Assert.Equal(newHeadObjectId, reference);

                Assert.Equal(newHeadObjectId, repository.GetHeadCommitSha());

                var headCommit = repository.GetHeadCommit();
                Assert.NotNull(headCommit);
                Assert.Equal(newHeadObjectId, headCommit.Value.Sha);
            }
        }

        // A fresh repository with no commits yet.
        [Fact]
        public void GetHeadMissingTest()
        {
            this.InitializeSourceControl(withInitialCommit: false);

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                var head = repository.GetHeadAsReferenceOrSha();
                var reference = Assert.IsType<string>(head);
                Assert.Equal("refs/heads/master", reference);

                Assert.Equal(GitObjectId.Empty, repository.GetHeadCommitSha());

                Assert.Null(repository.GetHeadCommit());
            }
        }

        // Fetch a commit from the object store
        [Fact]
        public void GetCommitTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(2);

            var headObjectId = GitObjectId.Parse(this.Repo.Head.Tip.Sha);

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                var commit = repository.GetCommit(headObjectId);
                Assert.Equal(headObjectId, commit.Sha);
            }
        }

        [Fact]
        public void GetInvalidCommitTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(2);

            var headObjectId = GitObjectId.Parse(this.Repo.Head.Tip.Sha);

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                Assert.Throws<GitException>(() => repository.GetCommit(GitObjectId.Empty));
            }
        }

        [Fact]
        public void GetTreeEntryTest()
        {
            this.InitializeSourceControl();
            File.WriteAllText(Path.Combine(this.RepoPath, "hello.txt"), "Hello, World");
            Commands.Stage(this.Repo, "hello.txt");
            this.AddCommits();

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                var headCommit = repository.GetHeadCommit();
                Assert.NotNull(headCommit);

                var helloBlobId = repository.GetTreeEntry(headCommit.Value.Tree, Encoding.UTF8.GetBytes("hello.txt"));
                Assert.Equal("1856e9be02756984c385482a07e42f42efd5d2f3", helloBlobId.ToString());
            }
        }

        [Fact]
        public void GetInvalidTreeEntryTest()
        {
            this.InitializeSourceControl();
            File.WriteAllText(Path.Combine(this.RepoPath, "hello.txt"), "Hello, World");
            Commands.Stage(this.Repo, "hello.txt");
            this.AddCommits();

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                var headCommit = repository.GetHeadCommit();
                Assert.NotNull(headCommit);

                Assert.Equal(GitObjectId.Empty, repository.GetTreeEntry(headCommit.Value.Tree, Encoding.UTF8.GetBytes("goodbye.txt")));
            }
        }

        [Fact]
        public void GetObjectByShaTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(2);

            var headObjectId = GitObjectId.Parse(this.Repo.Head.Tip.Sha);

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                var commitStream = repository.GetObjectBySha(headObjectId, "commit");
                Assert.NotNull(commitStream);

                var objectStream = Assert.IsType<GitObjectStream>(commitStream);
                Assert.Equal("commit", objectStream.ObjectType);
                Assert.Equal(186, objectStream.Length);
            }
        }

        [Fact]
        public void GetObjectByShaAndWrongTypeTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(2);

            var headObjectId = GitObjectId.Parse(this.Repo.Head.Tip.Sha);

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                Assert.Throws<GitException>(() => repository.GetObjectBySha(headObjectId, "tree"));
            }
        }

        [Fact]
        public void GetMissingObjectByShaTest()
        {
            this.InitializeSourceControl();
            this.AddCommits(2);

            var headObjectId = GitObjectId.Parse(this.Repo.Head.Tip.Sha);

            using (var repository = GitRepository.Create(this.RepoPath))
            {
                Assert.Throws<GitException>(() => repository.GetObjectBySha(GitObjectId.Parse("7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9"), "commit"));
                Assert.Null(repository.GetObjectBySha(GitObjectId.Empty, "commit"));
            }
        }

        private static void AssertPath(string expected, string actual)
        {
            Assert.Equal(
                Path.GetFullPath(expected),
                Path.GetFullPath(actual));
        }
    }
}
