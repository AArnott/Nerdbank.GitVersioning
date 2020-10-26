﻿namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// Represents an instruction in a deltified stream.
    /// </summary>
    /// <seealso href="https://git-scm.com/docs/pack-format#_deltified_representation"/>
    public struct DeltaInstruction
    {
        /// <summary>
        /// Gets or sets the type of the current instruction.
        /// </summary>
        public DeltaInstructionType InstructionType;

        /// <summary>
        /// If the <see cref="InstructionType"/> is <see cref="DeltaInstructionType.Copy"/>,
        /// the offset of the base stream to start copying from.
        /// </summary>
        public int Offset;

        /// <summary>
        /// The number of bytes to copy or insert.
        /// </summary>
        public int Size;
    }
}
