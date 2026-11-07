using System.Runtime.Serialization;

namespace KanaBootstrapper;

[DataContract]
internal class PlaceLaunchResponse
{
	[DataMember(Name = "jobId")]
	public string JobId { get; set; }

	[DataMember(Name = "status")]
	public int Status { get; set; }

	[DataMember(Name = "joinScriptUrl")]
	public string JoinScriptUrl { get; set; }

	[DataMember(Name = "authenticationUrl")]
	public string AuthenticationUrl { get; set; }

	[DataMember(Name = "authenticationTicket")]
	public string AuthenticationTicket { get; set; }

	[DataMember(Name = "joinScript")]
	public string JoinScript { get; set; }

	[DataMember(Name = "message")]
	public string Message { get; set; }
}
