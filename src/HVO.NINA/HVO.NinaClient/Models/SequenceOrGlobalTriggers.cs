using HVO;

namespace HVO.NinaClient.Models;

[NamedOneOf("Sequence", typeof(SequenceResponse), "GlobalTriggers", typeof(GlobalTriggersResponse))]
public partial class SequenceOrGlobalTriggers;
