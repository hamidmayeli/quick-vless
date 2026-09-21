const MAX_USAGE = 500 * 1024 * 1024

function createUsage(userId, date){
  return {
    "user_id": userId,
    "date": date,
    "uplink": 0,
    "downlink": 0,
    "total": Math.floor(Math.random() * MAX_USAGE),
  };
}

module.exports = function() {
  let db = {
    "users": [
      {
        "id": "user-1",
        "name": "Alice",
        "quota": 53687091200,
        "expiry": "2027-06-01",
        "single_connection": false,
        "enabled": true
      },
      {
        "id": "user-2",
        "name": "Bob",
        "quota": null,
        "expiry": null,
        "single_connection": true,
        "enabled": true
      },
      {
        "id": "user-3",
        "name": "Carol",
        "quota": 10737418240,
        "expiry": "2026-12-31",
        "single_connection": false,
        "enabled": false
      }
    ],
    "usage": [],
    "configs": [
      {
        "id": "user-1",
        "url": "vless://user-1@mock-server.example.com:443?type=tcp&security=reality&pbk=mock-public-key&fp=chrome&sni=dl.google.com&sid=ab12cd34#Alice"
      },
      {
        "id": "user-2",
        "url": "vless://user-2@mock-server.example.com:443?type=tcp&security=reality&pbk=mock-public-key&fp=chrome&sni=dl.google.com&sid=ab12cd34#Bob"
      },
      {
        "id": "user-3",
        "url": "vless://user-3@mock-server.example.com:443?type=tcp&security=reality&pbk=mock-public-key&fp=chrome&sni=dl.google.com&sid=ab12cd34#Carol"
      }
    ]
  }

  // Generate usage data for the past 30 days for each user
  let dateTime = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000)

  while (dateTime <= new Date()) {
    const dateStr = dateTime.toISOString();
    db.users.forEach(user => {
      db.usage.push(createUsage(user.id, dateStr));
    });

    // Add 5 minutes to the current dateTime
    dateTime = new Date(dateTime.getTime() + 5 * 60 * 1000);
  }

  return db;
}