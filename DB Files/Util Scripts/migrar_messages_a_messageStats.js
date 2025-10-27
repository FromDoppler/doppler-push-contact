// Configuración
const dbName = "push-int"; // <-- cambiar la base de datos segun corresponda
const sourceCollection = db.getSiblingDB(dbName).messages;
const targetCollection = db.getSiblingDB(dbName).messageStats;

// Definir el rango de fechas
const startDate = ISODate("2025-09-01T00:00:00.000Z"); // 1 de septiembre de 2025
const endDate = new Date(); // hasta ahora

// Pipeline de agregación
const pipeline = [
  {
    $match: {
      inserted_date: { $gte: startDate, $lte: endDate }
    }
  },
  {
    // Redondeamos la fecha a la hora
    $addFields: {
      truncated_date: {
        $dateTrunc: {
          date: "$inserted_date",
          unit: "hour"
        }
      }
    }
  },
  {
    // Preparamos la estructura para MessageStats
    $project: {
      _id: 0,
      date: "$truncated_date",
      message_id: 1,
      domain: 1,
      sent: 1,
      delivered: 1,
      not_delivered: 1,
      billable_sends: 1,
      received: 1,
      click: "$clicks", // el nombre en Messages es diferente a MessageStats
      action_click: { $literal: 0 } // no existe en Messages
    }
  }
];

// Ejecutar la agregación y bulk insert
const cursor = sourceCollection.aggregate(pipeline, { allowDiskUse: true });

const bulkOps = [];
let count = 0;

cursor.forEach(doc => {
  bulkOps.push({
    insertOne: { document: doc }
  });

  count++;

  // Para evitar exceder tamaño de batch de 1000
  if (bulkOps.length === 1000) {
    targetCollection.bulkWrite(bulkOps);
    bulkOps.length = 0;
  }
});

// Insertar los restantes
if (bulkOps.length > 0) {
  targetCollection.bulkWrite(bulkOps);
}

print(`✅ Migración completa. ${count} documentos insertados en MessageStats.`);
