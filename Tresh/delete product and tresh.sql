/****** Script for SelectTopNRows command from SSMS  ******/

  delete from [Olga2].[info].[ApprDocsTypes]
  where id>4

  delete from [Olga2].[product].[Products]
   where id>=29

SELECT TOP 1000 [Id]
      ,[ApprType]
  FROM [Olga2].[info].[ApprDocsTypes]
