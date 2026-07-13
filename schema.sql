CREATE TABLE "Users" (
    "Id" SERIAL PRIMARY KEY,
    "TelegramId" BIGINT NOT NULL UNIQUE,
    "Username" TEXT NULL,
    "FirstName" TEXT NULL
);

CREATE TABLE "Movies" (
    "Id" SERIAL PRIMARY KEY,
    "MovieCode" TEXT NOT NULL UNIQUE,
    "Title" TEXT NOT NULL,
    "FileId" TEXT NOT NULL,
    "Description" TEXT NULL,
    "MediaType" TEXT NULL,
    "PhotoFileId" TEXT NULL,
    "Views" INT NOT NULL DEFAULT 0,
    "CategoryId" INT NOT NULL DEFAULT 0
);
