﻿open Suave.Http
open Suave.Http.Applicatives
open Suave.Web
open Suave.Http.Writers
open Suave.Http.Successful
open Suave.Types
open Suave.Utils
open System
open Newtonsoft.Json
open Suave.Web.ParsingAndControl
open System.Collections.Generic
open Microsoft.FSharp.Quotations.Patterns
open Suave.Http.RequestErrors

[<CLIMutable>]
type Todo = 
  { title : string
    order : int
    completed : bool
    url : string }

let store = ResizeArray<Todo>()
let find id = store |> Seq.tryFind (fun t -> t.url.EndsWith id)
let serialize = JsonConvert.SerializeObject
let deserialize<'a> s = JsonConvert.DeserializeObject<'a> s
let CORS = 
  set_header "Access-Control-Allow-Origin" "*" >>= set_header "Access-Control-Allow-Headers" "content-type" 
  >>= set_header "Access-Control-Allow-Methods" "POST, GET, OPTIONS, DELETE, PATCH"

let get _ = 
  store
  |> serialize
  |> OK

let getTodo id =
  match find id with
  | Some todo -> serialize todo |> OK
  | None -> NOT_FOUND id

let patchTodo request = 
  let json = request.raw_form |> UTF8.to_string'
  let patchedPropertiesContains property = 
    JsonConvert.DeserializeObject<Dictionary<string, string>> json |> Seq.exists (fun x -> x.Key = property)
  let patchedTodo = json |> deserialize<Todo>
  
  let getPatchedProperty (property : Quotations.Expr<_>) patched original = 
    let propertyInfo = 
      match property with
      | Lambda(_, PropertyGet(_, propertyInfo, _)) -> propertyInfo
      | _ -> failwith "Property expression required"
    match patchedPropertiesContains propertyInfo.Name with
    | true -> patched
    | false -> original
    |> propertyInfo.GetValue
    |> unbox
  
  match find request.url with
  | Some todo ->
    store.Remove todo |> ignore
    let getPatchedProperty expr = getPatchedProperty expr patchedTodo todo
    
    let patched = 
      { todo with title = getPatchedProperty <@ fun t -> t.title @>
                  completed = getPatchedProperty <@ fun t -> t.completed @>
                  order = getPatchedProperty <@ fun t -> t.order @> }
    store.Add patched
    patched
    |> serialize
    |> OK
  | None -> NOT_FOUND request.url

let post request = 
  let todo = 
    request.raw_form
    |> UTF8.to_string'
    |> deserialize<Todo>
  
  let scheme = 
    if request.is_secure then "https"
    else "http"
  
  let host = 
    match HttpRequest.header request "host" with
    | Some host -> host
    | None -> failwith "Host header not found"
  
  let todo = { todo with url = Guid.NewGuid() |> sprintf "%s://%s/%O" scheme host }
  store.Add todo
  todo
  |> serialize
  |> OK

let deleteAll _ =
  store.Clear()
  store |> serialize |> OK

let deleteTodo id = 
  match find id with
  | Some todo ->
    store.Remove todo |> ignore
    OK id
  | None -> NOT_FOUND id

let routes = 
  choose [ OPTIONS >>= CORS >>= NO_CONTENT
           GET >>= CORS >>= url "/" >>= request get
           GET >>= CORS >>= url_scan "/%s" getTodo
           PATCH >>= CORS >>= parse_post_data >>= request patchTodo
           POST >>= CORS >>= parse_post_data >>= request post
           DELETE >>= CORS >>= url "/" >>= request deleteAll
           DELETE >>= CORS >>= url_scan "/%s" deleteTodo ]

[<EntryPoint>]
let main _ = 
  web_server default_config routes
  0