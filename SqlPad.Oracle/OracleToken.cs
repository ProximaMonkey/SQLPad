using System.Diagnostics;

namespace SqlPad.Oracle
{
	[DebuggerDisplay("OracleToken (Value={Value}, Index={Index})")]
	public struct OracleToken : IToken
	{
		public static OracleToken Empty = new OracleToken();
		public readonly CommentType CommentType;
		public readonly int Index;
		public readonly string Value;
		public readonly string UpperInvariantValue;

		public OracleToken(string value, int index, CommentType commentType = CommentType.None)
		{
			UpperInvariantValue = value.ToUpperInvariant();
			Value = value;
			Index = index;
			CommentType = commentType;
		}

		string IToken.Value => Value;

	    int IToken.Index => Index;

	    CommentType IToken.CommentType => CommentType;
	}
}