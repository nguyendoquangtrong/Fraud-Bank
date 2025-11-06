# Fraud-Bank

Đồ án môn Big Data mô phỏng hệ thống phát hiện gian lận giao dịch ngân hàng sử dụng Kafka (Redpanda), OpenSearch và PostgreSQL.

## Hạ tầng & dịch vụ

- PostgreSQL 16: lưu trữ tài khoản, giao dịch và outbox.
- Redpanda: cụm Kafka đơn node, dùng cho các topic giao dịch.
- OpenSearch + Dashboards: lưu projection và quan sát dữ liệu.
- Transaction/Fraud/Projection Service (ASP.NET) và Transaction UI (Node/Express + Socket.IO).

Khởi động toàn bộ stack:

```bash
docker compose up -d
```

## Khởi tạo Database (Postgres)

1. Kết nối tới Postgres (user/pass: `bank/bank`, db: `bankdb`):

    ```bash
    docker compose exec postgres psql -U bank -d bankdb
    ```

2. Tạo schema và các bảng cần thiết:

    ```sql
    CREATE EXTENSION IF NOT EXISTS "pgcrypto";

    CREATE TABLE IF NOT EXISTS "Accounts" (
      "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
      "AccountNo" varchar(32) NOT NULL UNIQUE,
      "HolderName" varchar(200),
      "Balance" numeric(18,2) NOT NULL DEFAULT 0,
      "CreatedAtUtc" timestamp without time zone NOT NULL DEFAULT now(),
      "UpdatedAtUtc" timestamp without time zone NOT NULL DEFAULT now()
    );

    CREATE TABLE IF NOT EXISTS "Transactions"(
      "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
      "TxId" text UNIQUE NOT NULL,
      "FromAccount" text NOT NULL,
      "ToAccount" text NOT NULL,
      "Amount" numeric(18,2) NOT NULL,
      "Type" text NOT NULL,
      "OldBalanceOrg" numeric(18,2) NOT NULL,
      "NewBalanceOrig" numeric(18,2) NOT NULL,
      "OldBalanceDest" numeric(18,2) NOT NULL,
      "NewBalanceDest" numeric(18,2) NOT NULL,
      "Status" text NOT NULL,
      "CreatedAtUtc" timestamptz NOT NULL
    );

    CREATE TABLE IF NOT EXISTS "Outbox"(
      "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
      "AggregateId" text NOT NULL,
      "Type" text NOT NULL,
      "Payload" jsonb NOT NULL,
      "CreatedAtUtc" timestamptz NOT NULL,
      "PublishedAtUtc" timestamptz
    );
    ```

3. Seed dữ liệu tài khoản mẫu (đảm bảo mỗi tài khoản chỉ insert một lần):

    ```sql
    INSERT INTO "Accounts" ("AccountNo","HolderName","Balance") VALUES
    ('A-001','Alice',  5000.00),
    ('A-002','Bob',    8000.00),
    ('A-003','Carol',  2000.00),
    ('B-001','Ben',    1000.00),
    ('B-002','Brian',  2000.00),
    ('B-003','Bella',  3000.00),
    ('C-001','Cindy',   800.00)
    ON CONFLICT ("AccountNo") DO NOTHING;
    ```

## Kafka Topics cần có

Các dịch vụ sử dụng 5 topic sau:

```
transactions.requested
transactions.scored
transactions.decided
transactions.ledger-applied
transactions.reviewed
```

Redpanda trong `docker-compose.yml` đã bật `REDPANDA_AUTO_CREATE_TOPICS=true`, nhưng nên tạo sẵn để kiểm soát cấu hình:

```bash
docker compose exec redpanda rpk topic create \
  transactions.requested \
  transactions.scored \
  transactions.decided \
  transactions.ledger-applied \
  transactions.reviewed

docker compose exec redpanda rpk topic list
```

## Khởi tạo OpenSearch index

Projection Service sẽ tự tạo index `tx-logs` nếu chưa tồn tại, tuy nhiên có thể chủ động khởi tạo để kiểm tra mapping:

```bash
curl -XPUT http://localhost:9200/tx-logs \
  -H 'Content-Type: application/json' \
  -d '{
    "settings": { "number_of_shards": 1, "number_of_replicas": 0 },
    "mappings": {
      "properties": {
        "transactionId": { "type": "keyword" },
        "status":        { "type": "keyword" },
        "risk":          { "type": "double"  },
        "amount":        { "type": "double"  },
        "fromAccount":   { "type": "keyword" },
        "toAccount":     { "type": "keyword" },
        "createdAtUtc":  { "type": "date"    }
      }
    }
  }'
```

Xác nhận index:

```bash
curl http://localhost:9200/tx-logs?pretty
```

## Gợi ý quy trình chạy thử

1. Khởi động stack `docker compose up -d`.
2. Tạo schema + seed tài khoản theo hướng dẫn.
3. Tạo các Kafka topic cần thiết.
4. Mở UI `ui/transaction-ui` (README riêng) để gửi giao dịch thử và theo dõi trên OpenSearch Dashboards (`http://localhost:5601`).
