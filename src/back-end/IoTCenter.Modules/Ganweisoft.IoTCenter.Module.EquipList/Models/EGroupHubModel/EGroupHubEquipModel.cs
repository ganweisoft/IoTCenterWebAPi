using System.Collections.Generic;

namespace Ganweisoft.IoTCenter.Module.EquipList;

public class EGroupHubEquipModel
{
    public string Name { get; set; }
    public int Id { get; set; }
}

public class EGroupHubGroupModel
{
    public int GroupId { get; set; }

    public IEnumerable<EGroupHubEquipModel> Equips { get; set; }
}
