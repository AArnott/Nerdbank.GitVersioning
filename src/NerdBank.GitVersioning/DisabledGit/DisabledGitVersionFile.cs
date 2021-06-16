namespace Nerdbank.GitVersioning
{
    using Validation;

    internal class DisabledGitVersionFile : VersionFile
    {
        public DisabledGitVersionFile(GitContext context)
            : base(context)
        {
        }

        protected new DisabledGitContext Context => (DisabledGitContext)base.Context;

        protected override VersionOptions GetVersionCore(out string actualDirectory)
        {
            actualDirectory = null;
            return null;
        }
    }
}
