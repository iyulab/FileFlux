# Message API guide

This guide describes how a client reads and sends messages. It is written the way many API documents are: numbered steps with
code samples under them, headings that name an endpoint with a path parameter, and points that are wrapped over several lines.

## Setup

Follow the steps in order. Each step has a sample.

1. Install the command line tool

   ```bash
   cd build

   make all


   make package
   ```

2. Send the browser to the authorization endpoint
   and wait for the redirect back to the client.

   A second paragraph of the step explains what the redirect carries.

3. Show the consent screen with the requested scopes

   > ```text
   > first
   >
   > second
   > ```

## Options `{...}`

The options object carries the settings of a call. It has several members that are explained below in prose so that the section has a body of its own.

### Message status — `GET /messages/{id}`

Returns the status of one message. The identifier comes from the create call. Use the `{id}` placeholder exactly as shown, and
write {kind} for the kind of the message.

```json
{
  "id": "m1",

  "status": "sent"
}
```

## Design notes

옵션이 여럿일 때 ① 근본적 해결 ② 정석적인 방법
③ 기술부채를 남기지 않는 방법을 우선한다. 추천의 근거는 정석 부합
비교이고, 변경이 적은 쪽이 아니다.

## Closing

The last section closes the guide. Closed vs. open models are compared in the benchmarks, and the numbers are given there.
