using System;
namespace WarCore.Sim
{
    public readonly struct GameplayCapturedAttribute
    {
        public readonly ulong Key; public readonly long Raw;
        public GameplayCapturedAttribute(ulong key,long raw){Key=key;Raw=raw;}
    }
    public readonly struct GameplayCapturedAction
    {
        public readonly ulong DefinitionKey,DamageTypeKey,PipelineKey; public readonly uint Ordinal;
        public GameplayCapturedAction(ulong definitionKey,uint ordinal,ulong damageTypeKey,ulong pipelineKey)
        {DefinitionKey=definitionKey;Ordinal=ordinal;DamageTypeKey=damageTypeKey;PipelineKey=pipelineKey;}
    }
    // Inline value storage makes each released status independent of reusable cast slots.
    public struct GameplaySourceSnapshot
    {
        public const int MaximumAttributes=32,MaximumActions=32;
        public readonly GameplayStamp Source;public readonly long CapturedTick;public readonly ulong AbilityKey;
        private int attributeCount,actionCaptureCount;
        public int AttributeCount=>attributeCount; public int ActionCaptureCount=>actionCaptureCount;
        private GameplayCapturedAttribute attribute0,attribute1,attribute2,attribute3,attribute4,attribute5,attribute6,attribute7,attribute8,attribute9,attribute10,attribute11,attribute12,attribute13,attribute14,attribute15,attribute16,attribute17,attribute18,attribute19,attribute20,attribute21,attribute22,attribute23,attribute24,attribute25,attribute26,attribute27,attribute28,attribute29,attribute30,attribute31;
        private GameplayCapturedAction action0,action1,action2,action3,action4,action5,action6,action7,action8,action9,action10,action11,action12,action13,action14,action15,action16,action17,action18,action19,action20,action21,action22,action23,action24,action25,action26,action27,action28,action29,action30,action31;
        public GameplaySourceSnapshot(GameplayStamp source,long capturedTick):this(source,capturedTick,0){}
        public GameplaySourceSnapshot(GameplayStamp source,long capturedTick,ulong abilityKey)
        {this=default;Source=source;CapturedTick=capturedTick;AbilityKey=abilityKey;}
        public GameplayCapturedAttribute AttributeAt(int index)
        {switch(index){
            case 0:return attribute0;
            case 1:return attribute1;
            case 2:return attribute2;
            case 3:return attribute3;
            case 4:return attribute4;
            case 5:return attribute5;
            case 6:return attribute6;
            case 7:return attribute7;
            case 8:return attribute8;
            case 9:return attribute9;
            case 10:return attribute10;
            case 11:return attribute11;
            case 12:return attribute12;
            case 13:return attribute13;
            case 14:return attribute14;
            case 15:return attribute15;
            case 16:return attribute16;
            case 17:return attribute17;
            case 18:return attribute18;
            case 19:return attribute19;
            case 20:return attribute20;
            case 21:return attribute21;
            case 22:return attribute22;
            case 23:return attribute23;
            case 24:return attribute24;
            case 25:return attribute25;
            case 26:return attribute26;
            case 27:return attribute27;
            case 28:return attribute28;
            case 29:return attribute29;
            case 30:return attribute30;
            case 31:return attribute31;
            default:throw new SimBoundaryException("M0C_SOURCE_CAPTURE_INDEX");}}
        internal GameplaySourceSnapshot WithAttribute(GameplayCapturedAttribute value)
        {var copy=this;switch(copy.attributeCount){
            case 0:copy.attribute0=value;break;
            case 1:copy.attribute1=value;break;
            case 2:copy.attribute2=value;break;
            case 3:copy.attribute3=value;break;
            case 4:copy.attribute4=value;break;
            case 5:copy.attribute5=value;break;
            case 6:copy.attribute6=value;break;
            case 7:copy.attribute7=value;break;
            case 8:copy.attribute8=value;break;
            case 9:copy.attribute9=value;break;
            case 10:copy.attribute10=value;break;
            case 11:copy.attribute11=value;break;
            case 12:copy.attribute12=value;break;
            case 13:copy.attribute13=value;break;
            case 14:copy.attribute14=value;break;
            case 15:copy.attribute15=value;break;
            case 16:copy.attribute16=value;break;
            case 17:copy.attribute17=value;break;
            case 18:copy.attribute18=value;break;
            case 19:copy.attribute19=value;break;
            case 20:copy.attribute20=value;break;
            case 21:copy.attribute21=value;break;
            case 22:copy.attribute22=value;break;
            case 23:copy.attribute23=value;break;
            case 24:copy.attribute24=value;break;
            case 25:copy.attribute25=value;break;
            case 26:copy.attribute26=value;break;
            case 27:copy.attribute27=value;break;
            case 28:copy.attribute28=value;break;
            case 29:copy.attribute29=value;break;
            case 30:copy.attribute30=value;break;
            case 31:copy.attribute31=value;break;
            default:throw new SimBoundaryException("M0C_SOURCE_CAPTURE_CAPACITY");}copy.attributeCount++;return copy;}
        public GameplayCapturedAction ActionCaptureAt(int index)
        {switch(index){
            case 0:return action0;
            case 1:return action1;
            case 2:return action2;
            case 3:return action3;
            case 4:return action4;
            case 5:return action5;
            case 6:return action6;
            case 7:return action7;
            case 8:return action8;
            case 9:return action9;
            case 10:return action10;
            case 11:return action11;
            case 12:return action12;
            case 13:return action13;
            case 14:return action14;
            case 15:return action15;
            case 16:return action16;
            case 17:return action17;
            case 18:return action18;
            case 19:return action19;
            case 20:return action20;
            case 21:return action21;
            case 22:return action22;
            case 23:return action23;
            case 24:return action24;
            case 25:return action25;
            case 26:return action26;
            case 27:return action27;
            case 28:return action28;
            case 29:return action29;
            case 30:return action30;
            case 31:return action31;
            default:throw new SimBoundaryException("M0C_SOURCE_CAPTURE_INDEX");}}
        internal GameplaySourceSnapshot WithActionCapture(GameplayCapturedAction value)
        {var copy=this;switch(copy.actionCaptureCount){
            case 0:copy.action0=value;break;
            case 1:copy.action1=value;break;
            case 2:copy.action2=value;break;
            case 3:copy.action3=value;break;
            case 4:copy.action4=value;break;
            case 5:copy.action5=value;break;
            case 6:copy.action6=value;break;
            case 7:copy.action7=value;break;
            case 8:copy.action8=value;break;
            case 9:copy.action9=value;break;
            case 10:copy.action10=value;break;
            case 11:copy.action11=value;break;
            case 12:copy.action12=value;break;
            case 13:copy.action13=value;break;
            case 14:copy.action14=value;break;
            case 15:copy.action15=value;break;
            case 16:copy.action16=value;break;
            case 17:copy.action17=value;break;
            case 18:copy.action18=value;break;
            case 19:copy.action19=value;break;
            case 20:copy.action20=value;break;
            case 21:copy.action21=value;break;
            case 22:copy.action22=value;break;
            case 23:copy.action23=value;break;
            case 24:copy.action24=value;break;
            case 25:copy.action25=value;break;
            case 26:copy.action26=value;break;
            case 27:copy.action27=value;break;
            case 28:copy.action28=value;break;
            case 29:copy.action29=value;break;
            case 30:copy.action30=value;break;
            case 31:copy.action31=value;break;
            default:throw new SimBoundaryException("M0C_SOURCE_CAPTURE_CAPACITY");}copy.actionCaptureCount++;return copy;}
        public bool TryAttribute(ulong key,out long raw)
        {for(int i=0;i<AttributeCount;i++){var a=AttributeAt(i);if(a.Key==key){raw=a.Raw;return true;}}raw=0;return false;}
    }
}
