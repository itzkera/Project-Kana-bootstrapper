using System;

namespace KanaBootstrapper;

internal class JoinLinkException : Exception
{
	public JoinLinkException(string message)
		: base(message)
	{
	}
}
