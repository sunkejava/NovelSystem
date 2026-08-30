export interface Novel{ id:number;title:string;sourceFile:string;status:string;createdAt:string;content?:string }
export interface Character{ id:number;novelId:number;name:string;gender?:string;personality?:string;description?:string;voiceProfileId?:number;voiceFile?:string }
export interface ScriptLine{ id:number;novelId:number;characterId?:number;order:number;speaker:string;text:string;emotion?:string;audioFile?:string;status:string }
export interface JobRecord{ id:number;type:string;status:string;progress:number;result?:string;error?:string;createdAt:string;startedAt?:string;finishedAt?:string }
export interface VoiceOption{name:string;path:string}
export interface VoiceProfile{id:number;name:string;referenceAudioFile:string;referenceText:string;useXVector:boolean;language:string;promptFile?:string;status:string;createdAt:string;updatedAt:string}
export interface WritingStyle{id:number;novelId?:number;name:string;summary:string;promptTemplate:string}
export interface GeneratedNovel{id:number;title:string;styleId?:number;sourceNovelId?:number;prompt:string;content:string;createdAt:string}