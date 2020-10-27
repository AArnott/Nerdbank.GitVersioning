﻿#nullable enable

using System;

namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// Represents the signature of a Git committer or author.
    /// </summary>
    public struct GitSignature
    {
        /// <summary>
        /// Gets or sets the name of the committer or author.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the e-mail address of the commiter or author.
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Gets or sets the date and time at which the commit was made.
        /// </summary>
        public DateTimeOffset Date { get; set; }
    }
}
