using System.Collections.Generic;

namespace WarCore.Tests
{
    // Values recorded on the unmodified builder; see AssemblyRecorder for how they were produced. Never edit by hand.
    internal static class AssemblyGolden
    {
        internal static readonly string[] Lines =
        {
            "M2b|snap0|8BEB4D2AFE88F39083DDF3AFDA1105A1269079DB3EFF8F9A8681D0560E5422E4",
            "M2b|components|interim.collider:2:3;interim.move-intent:2001:1;interim.position:1:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "M2b|systems|interim.orders|0|10;interim.locomotion|1|20",
            "M2b|creation|6:A3F9F5C60BB6F18B10680C90873C1D89E271748F712EDC9C89532F718DBDD756",
            "M2b|snap1|201A67AE85D80352409E5BCA8DE6A293ED1BB8E3AC284FF878773441323FF8B0",
            "M2b|snap100|3EAD5721041E16C1422211C06C63A82BEF82F95E0B498B1A5D0B12B37580CC89",
            "M2b|snapReverse0|8BEB4D2AFE88F39083DDF3AFDA1105A1269079DB3EFF8F9A8681D0560E5422E4",
            "M2b|creationReverse|6:C36503ECA245450C4E9E012BC49F3A603339256F42D1F8A797E0CED989F5B107",
            "M3|snap0|2E987E9E7A84EB665323912D5076B7DBB0C01FADD91FA4D677189D6CB88DC9BA",
            "M3|components|interim.collider:2:3;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "M3|systems|interim.orders|0|10;interim.locomotion|1|20",
            "M3|creation|6:A3F9F5C60BB6F18B10680C90873C1D89E271748F712EDC9C89532F718DBDD756",
            "M3|snap1|6FC80A41F932088A095968954BA493842027B7DED53F6EC6AF646A2F9C1C90DF",
            "M3|snap100|344AF0BB221B4C7A0E08E8E9397EC1DFBD9857E9F63FF73E94CE4B30CA195632",
            "M3|snapReverse0|2E987E9E7A84EB665323912D5076B7DBB0C01FADD91FA4D677189D6CB88DC9BA",
            "M3|creationReverse|6:C36503ECA245450C4E9E012BC49F3A603339256F42D1F8A797E0CED989F5B107",
            "M4|snap0|57C6783D8C6EDE63EE72DA1C2DCAE17189E94AEAB575823A5B420C7474017783",
            "M4|components|interim.collider:2:3;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "M4|systems|interim.orders|0|10;interim.locomotion|1|20",
            "M4|creation|24:9D5CC34F7B1CBB3CEC2B4C462F9443185A0F2B4F3921A05A5BD72A240514DEEF",
            "M4|snap1|DCC4C7F38E63FE7A2626819EB65D377EA60160F467A5A3F886DA6B7CD7221363",
            "M4|snap100|35A8F75A9397FCFE73F95B0CBA0F6FAC5A1747A482750A81F8472E7D6621053B",
            "M4|snapReverse0|57C6783D8C6EDE63EE72DA1C2DCAE17189E94AEAB575823A5B420C7474017783",
            "M4|creationReverse|24:D54B86A93B6F1DB2A0A6F1310B13AD5C34F1C5038ED479034233A9176D955E23",
            "M5|snap0|7C7685B5C3C5AB16A43C485D47EAD226D883CD18B4D09ED9686D39B47C8F3D6A",
            "M5|components|combat.combatant:3001:2;interim.collider:2:3;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.gameplay-binding:21:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "M5|systems|bridge.dead-cleanup|0|5;interim.orders|0|10;interim.locomotion|1|20;combat.system|3|30",
            "M5|creation|24:9D5CC34F7B1CBB3CEC2B4C462F9443185A0F2B4F3921A05A5BD72A240514DEEF",
            "M5|snap1|927F4663F1EFF846A88F51B64DB06F141C2CEE8ADD231F95F3278E7EC7C4F137",
            "M5|snap100|3B1548D0D051222BCCC108EB41B12B8D3811E311C79ECEAED4124029C85165ED",
            "M5|snapReverse0|7C7685B5C3C5AB16A43C485D47EAD226D883CD18B4D09ED9686D39B47C8F3D6A",
            "M5|creationReverse|24:D54B86A93B6F1DB2A0A6F1310B13AD5C34F1C5038ED479034233A9176D955E23",
            "M6|snap0|BD8B4EF423711A3AFDAC5C28E18AC9AA5B5FD2C336BF28C51BF3284779AE644C",
            "M6|components|combat.combatant:3001:2;interim.collider:2:3;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.gameplay-binding:21:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "M6|systems|bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50",
            "M6|creation|84:C72FF08FD9A5C1079EBBCC6AF29CF115224E60B793739D5AB8A6B8F717C3D2E4",
            "M6|snap1|3F40F93B9FB4531BFF6B86C285C7D93538FA0459E3108014E5C5BBF5EBEB2A6A",
            "M6|snap100|FA0337B88C6021565002B5EED0B7D3F6D68DF9E7CF1EE6EBB7C6AA9C0FD64D6C",
            "M6|snapReverse0|BD8B4EF423711A3AFDAC5C28E18AC9AA5B5FD2C336BF28C51BF3284779AE644C",
            "M6|creationReverse|84:D4C7468BB4C82968E625EACB270DE71C977A72B5D08BC1AFB486D6AC8242F4CA",
            "M7|snap0|0CC6D165BDF7D0F38A904EDA3BEBD7A90F26604EF73A0177FEC43DF322B24D6B",
            "M7|components|combat.combatant:3001:2;interim.collider:2:3;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.gameplay-binding:21:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "M7|systems|bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50",
            "M7|creation|306:33397CECD41F2AE914FD86E7DE024DF4DDCAA894B5A3300998D6767C5F12D39C",
            "M7|snap1|247A009953A1F95A5A9253276B4B390FC800BCAA6C876FFFE4B0878959579044",
            "M7|snap100|909048CDC054557C3B9EE30585CA7477425C572E1C9796E83C06022D2CC3F3C8",
            "M7|snapReverse0|0CC6D165BDF7D0F38A904EDA3BEBD7A90F26604EF73A0177FEC43DF322B24D6B",
            "M7|creationReverse|306:FFAF795ECD6713F3EAF3135016C0765F6538CC7558662050A82EA08BC39F0401",
            "MeleeDuel|snap0|5D0506E2312F8E753FAA9DD7B80A201196DAA56039BF919710F35474999763F3",
            "MeleeDuel|components|combat.combatant:3001:2;combat.melee-state:3002:1;interim.collider:2:3;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.gameplay-binding:21:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "MeleeDuel|systems|bridge.dead-cleanup|0|5;interim.orders|0|10;interim.locomotion|1|20;combat.system|3|30",
            "MeleeDuel|creation|2:5B61071EB1F06CC3EBFA584B94D4FCA108C21AFADE0367798786D56A727D6BBF",
            "MeleeDuel|snap1|CAB38CB90AA22D73F62165D9A420638F36BC742745ABC31F3E782C29B227B6DC",
            "MeleeDuel|snap100|4B9168DCC81A2679C1124259F2C8BF624CAA441A298DB5F1F21ECE9C54C566CC",
            "MeleeDuel|snapReverse0|5D0506E2312F8E753FAA9DD7B80A201196DAA56039BF919710F35474999763F3",
            "MeleeDuel|creationReverse|2:1F41871446A2F458459F3F1A3AC4C0726286AB929322EA19DA0D258C9325DF7C",
            "MpDemo|snap0|4142605CB2B15CE75488CBD3FE3233A9C18BC5841BA2AE0C23D92C3FC96F25BE",
            "MpDemo|components|combat.combatant:3001:2;interim.collider:2:3;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.gameplay-binding:21:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "MpDemo|systems|bridge.dead-cleanup|0|5;control.fallback|0|6;control.player|0|7;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;interim.locomotion|1|20;control.player-fire|3|29;combat.system|3|30;battle.capture|3|40;battle.match|3|50",
            "MpDemo|creation|306:33397CECD41F2AE914FD86E7DE024DF4DDCAA894B5A3300998D6767C5F12D39C",
            "MpDemo|snap1|A130A5184DE8E5EC40BC703D4BA4534F3DE50BE94AACD2E6587951693BFE9981",
            "MpDemo|snap100|BB6F6BA7E657F50A82BB53FBB16D2CA91366E20F564C28689F7EABE3A8F8ABD8",
            "MpDemo|snapReverse0|4142605CB2B15CE75488CBD3FE3233A9C18BC5841BA2AE0C23D92C3FC96F25BE",
            "MpDemo|creationReverse|306:FFAF795ECD6713F3EAF3135016C0765F6538CC7558662050A82EA08BC39F0401",
            "Terrain|snap0|A4DAF66C00CD5C8852CAE330E32F61E62C08BA240ED99162D8BC570E8871D72C",
            "Terrain|components|interim.collider:2:3;interim.move-intent:2001:1;interim.move-progress:2003:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "Terrain|systems|interim.orders|0|10;interim.locomotion|1|20",
            "Terrain|creation|4:3850498F44C1F46CBF58A778DF45DFC9BE2CC7D0C98E48967FA062C6FA3063EF",
            "Terrain|snap1|1ABCA3128832F17641B97EB4827CF4C2A628C5C5CD2ABDB1B75DC14C24F24CFD",
            "Terrain|snap100|E65F0F8B9F67418BBFC5BC3F2020072B56FA5B991A69452B57BD6B9B4E3FE6BB",
            "Terrain|snapReverse0|A4DAF66C00CD5C8852CAE330E32F61E62C08BA240ED99162D8BC570E8871D72C",
            "Terrain|creationReverse|4:00A4D48CC4BCDBFEC749AE54041886F41611D14739D35EA3394F27E89525ACA2",
            "FacingMotionStanding|snap0|9D19D2BDB28530A72ACF760C89B70D60C0D05DA75AC3BCFDE03878B214688778",
            "FacingMotionStanding|components|interim.collider:2:3;interim.facing:2004:1;interim.motion-limits:2005:1;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;interim.standing:2006:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "FacingMotionStanding|systems|interim.orders|0|10;interim.locomotion|1|20",
            "FacingMotionStanding|creation|6:60E0A66AF036D2A9A83A54D009A9F5B58EFE979C62482FB1D8D710EA4E4BFC8D",
            "FacingMotionStanding|snap1|815FCD54DE34A1DE4A9155009BAD815D8EBEB3EFCEA2DD8DD646444B111DC92D",
            "FacingMotionStanding|snap100|833AF9D3385DD6C0F17E15419A946289947C7104CBD53BF8B885C2C5FDA92E26",
            "FacingMotionStanding|snapReverse0|9D19D2BDB28530A72ACF760C89B70D60C0D05DA75AC3BCFDE03878B214688778",
            "FacingMotionStanding|creationReverse|6:58987DE4A3FE8B957DBEC84C706B5870E0597FA035147A7FA97666CC1276D7EE",
            "BlockAi|snap0|C32BDE1290CBBC1D35EF404027AB608E1026EAAAF5F97DDC3B2BF77435D2EEC9",
            "BlockAi|components|combat.block-state:3003:1;combat.combatant:3001:2;combat.melee-state:3002:1;interim.collider:2:3;interim.facing:2004:1;interim.move-intent:2001:1;interim.move-waypoint:2002:1;interim.position:1:1;warcore.gameplay-binding:21:1;warcore.position-request:12:1;warcore.spatial-attachment:13:1;warcore.spawn-capture:1002:1;warcore.spawn-sequence:1001:1",
            "BlockAi|systems|bridge.dead-cleanup|0|5;interim.orders|0|10;battle.block-ai|0|11;interim.locomotion|1|20;combat.system|3|30",
            "BlockAi|creation|3:1548F1DA05D5B327A2A2DEB024AB65CF92A09B7D1A56214880F84E63650DD1E3",
            "BlockAi|snap1|50B1767BA151E7D9921D32F30BFEAB54F69A1D0F9CAA2E078C6416B63A688101",
            "BlockAi|snap100|684B064168851A1C82FE5114AAD113EAB35BACB5A37005DB98825A15CED4968D",
            "BlockAi|snapReverse0|C32BDE1290CBBC1D35EF404027AB608E1026EAAAF5F97DDC3B2BF77435D2EEC9",
            "BlockAi|creationReverse|3:DAE9738738B72046FE63E0C471B3ED31EBC9C1EB80E6996ACCA0C3035BD274D6",
        };

        internal static Dictionary<string, string> Of(string world)
        {
            var result = new Dictionary<string, string>();
            foreach (string line in Lines)
            {
                string[] parts = line.Split(new[] { '|' }, 3);
                if (parts[0] == world) result[parts[1]] = parts[2];
            }
            return result;
        }
    }
}
