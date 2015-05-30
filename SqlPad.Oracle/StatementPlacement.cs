namespace SqlPad.Oracle
{
	public enum StatementPlacement
	{
		None,
		ValuesClause,
		SelectList,
		TableReference,
		PivotClause,
		Where,
		GroupBy,
		Having,
		Join,
		OrderBy,
		Model,
		ConnectBy,
		RecursiveSearchOrCycleClause
	}
}
