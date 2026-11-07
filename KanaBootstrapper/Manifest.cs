using System.Collections.Generic;
using System.Runtime.Serialization;

namespace KanaBootstrapper;

[DataContract]
internal class Manifest
{
	[DataMember(Name = "available")]
	public bool Available { get; set; }

	[DataMember(Name = "format")]
	public int Format { get; set; }

	[DataMember(Name = "version")]
	public string Version { get; set; }

	[DataMember(Name = "size")]
	public long Size { get; set; }

	[DataMember(Name = "url")]
	public string Url { get; set; }

	[DataMember(Name = "packages")]
	public List<Package> Packages { get; set; }
}
