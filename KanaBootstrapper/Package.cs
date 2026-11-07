using System.Collections.Generic;
using System.Runtime.Serialization;

namespace KanaBootstrapper;

[DataContract]
internal class Package
{
	[DataMember(Name = "name")]
	public string Name { get; set; }

	[DataMember(Name = "sha256")]
	public string Sha256 { get; set; }

	[DataMember(Name = "size")]
	public long Size { get; set; }

	[DataMember(Name = "files")]
	public int Files { get; set; }

	[DataMember(Name = "dirs")]
	public List<string> Dirs { get; set; }

	[DataMember(Name = "url")]
	public string Url { get; set; }
}
