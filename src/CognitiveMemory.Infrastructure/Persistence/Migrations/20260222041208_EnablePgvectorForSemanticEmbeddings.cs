using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CognitiveMemory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnablePgvectorForSemanticEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS vector;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "SemanticClaimEmbeddings"
                ADD COLUMN IF NOT EXISTS "Embedding" vector;
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_SemanticClaimEmbeddings_Model_Dimensions"
                ON "SemanticClaimEmbeddings" ("ModelId", "Dimensions");
                """);

            // Optional ANN indexes for common embedding sizes used by this project.
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_SemanticClaimEmbeddings_Embedding_Hnsw_768"
                ON "SemanticClaimEmbeddings"
                USING hnsw (("Embedding"::vector(768)) vector_cosine_ops)
                WHERE "Dimensions" = 768;
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_SemanticClaimEmbeddings_Embedding_Hnsw_1536"
                ON "SemanticClaimEmbeddings"
                USING hnsw (("Embedding"::vector(1536)) vector_cosine_ops)
                WHERE "Dimensions" = 1536;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_SemanticClaimEmbeddings_Embedding_Hnsw_1536";
                """);

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_SemanticClaimEmbeddings_Embedding_Hnsw_768";
                """);

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_SemanticClaimEmbeddings_Model_Dimensions";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "SemanticClaimEmbeddings"
                DROP COLUMN IF EXISTS "Embedding";
                """);
        }
    }
}
