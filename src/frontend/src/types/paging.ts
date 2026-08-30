export interface PageQuery{
  page:number;
  pageSize:number;
  keyword?:string;
  status?:string;
  type?:string;
  speaker?:string;
  language?:string;
}
export interface PagedResult<T>{
  items:T[];
  total:number;
  page:number;
  pageSize:number;
  summary?:Record<string,number>;
}